"""从授权的原版脚本提取真实直播入口，生成续玩目录与原生 Yarn 前置条件。"""
import argparse
import json
import pathlib
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')

ROOT = pathlib.Path(__file__).resolve().parents[1]
NAMES = dict(NoBody='大安哥', Soldier='卡西迪', Terrorist='艾曼', Victim='哈妮', Judge='迈尔斯',
             Masturbator='米娅', Gachi='琪琪', Boss='匿名', MaoGirl='罗莎', Muslim='阿日', Vmz='V',
             BotChecker='菲利克斯', Commissar='娜斯佳', Gothic='瓦里娅', Mesuga='一之濑瑠夏',
             Veteran='科兹洛夫', Cop='科尔瓦斯基', MoldLand='真菌兰德', Nana='娜娜', Nerd='纪子', Journalist='加里克')


def requirement_text(expression):
    requirements = []
    for title in re.findall(r'visited\("([^"]+)"\)', expression):
        base, _, variant = title.partition('.')
        character = base.removeprefix('Entry_').removesuffix('_1')
        requirements.append('已触发' + NAMES.get(character, {'Greta': '环保行动相关事件'}.get(character, character))
                            + {'one': '的第一次连线', 'two': '的第二次连线', 'three_commi': '的共产国际去向事件'}.get(variant, '的前序事件'))
    descriptions = {
        '$Fan_Join': '已经加入粉丝群', '$Gachi_LeftDisappointed': '琪琪没有失望离开',
        '$MoldLand_Mission_Transport': '已接受真菌兰德的货运委托',
        '$MaoGirl_CState': '罗莎仍处于可以连线的状态', '$Forecast_Join': '已满足罗莎后续事件的参与条件',
        '$Know_Nana': '已经认识娜娜',
    }
    for variable, description in descriptions.items():
        if variable in expression:
            requirements.append(description)
    choice = re.search(r'\$BotChecker_Case\s*==\s*"([^"]+)"', expression)
    if choice:
        requirements.append('菲利克斯的前序选择为“' + dict(GetOff='下车', Surrender='投降', West='前往西部', Arid='前往荒地')[choice[1]] + '”')
    return '；'.join(requirements) or '原剧情前置条件满足'


def nodes_in(path, yarn_root):
    text = path.read_text(encoding="utf-8-sig")
    result = []
    for match in re.finditer(r"(?m)^title\s*:\s*(\S+)\s*$", text):
        end = re.search(r"(?m)^===\s*$", text[match.end():])
        if not end:
            continue
        block = text[match.start():match.end() + end.end()]
        if "---" not in block:
            continue
        header, body = block.split("---", 1)
        fields = {}
        for key, value in re.findall(r"(?m)^(\w+)\s*:\s*(.*?)\s*$", header):
            fields.setdefault(key.lower(), []).append(value)
        result.append(dict(title=fields["title"][0], subtitle=fields.get("subtitle", [""])[0],
                           when=fields.get("when", []), character=fields.get("character", [""])[0],
                           file=path.relative_to(yarn_root).as_posix(), body=body,
                           line=text[:match.start()].count("\n") + 1))
    return result


def narrative_condition(conditions):
    # 仅移除排期与候选数量判断；其他布尔表达式完整交给 Yarn 自己求值。
    parts = []
    for expression in conditions:
        if expression in ("once", "always"):
            continue
        expression = re.sub(r'CountAvailable\("[^"]+"\)\s*>=\s*1', 'true', expression)
        expression = re.sub(r'\$Today\s*-\s*\$\w+_LastTalkDay\s*>=\s*\d+', 'true', expression)
        expression = re.sub(r'\$(?:Today|CurrentHour|LineCount)\s*(?:>=|<=|==|>|<)\s*(?:\d+|\$\w+)', 'true', expression)
        expression = re.sub(r'\$Minor_Token\s*==\s*true', 'true', expression)
        parts.append('(' + expression + ')')
    return ' and '.join(parts) or 'true'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game', required=True)
    args = parser.parse_args()
    yarn_root = pathlib.Path(args.game) / 'Aistalt_Data/StreamingAssets/Mods/MyMod/Yarn'
    nodes = [node for path in sorted(yarn_root.rglob('*.yarn')) for node in nodes_in(path, yarn_root)
             if not any(p.lower() in ('storage', 'bin', 'ends', 'forshot') for p in path.parts)]
    by_title = {}
    for node in nodes:
        by_title.setdefault(node['title'], []).append(node)
    events = []
    for entry in nodes:
        if not entry['title'].startswith('Live_Entry_'):
            continue
        jumps = re.findall(r'<<jump\s+(Entry_\w+)\s*>>', entry['body'])
        if len(jumps) != 1 or jumps[0] not in by_title:
            raise ValueError('直播入口不能唯一映射事件：' + entry['title'])
        for target in by_title[jumps[0]]:
            index = len(events)
            condition = narrative_condition(entry['when'] + target['when'])
            variant = target['subtitle']
            suffix = {'one': '第一次连线', 'two': '第二次连线', 'three_war': '后续：战争',
                      'three_commi': '后续：共产国际', 'three_main': '后续：西部', 'three_arid': '后续：荒地'}.get(variant, '随机连线')
            events.append(dict(id='e' + str(index), entry=entry['title'], group=target['title'],
                               subtitle=target['subtitle'], character=entry['character'],
                               displayName=NAMES.get(entry['character'], entry['character']) + ' · ' + suffix,
                               requirement=requirement_text(condition),
                               file=target['file'], line=target['line'], entryFile=entry['file'],
                               originalConditions=entry['when'] + target['when'],
                               condition=condition,
                               oneShotEntry='once' in entry['when']))
    (ROOT / 'data/events.json').write_text(json.dumps(events, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    # 该文件仅保留原作中文显示辅助；实际候选和条件由运行时合并后的角色 Mod 动态生成。
    print('已提取 ' + str(len(events)) + ' 个随机直播事件，来自 ' + str(len(set(e['entry'] for e in events))) + ' 个入口。')


if __name__ == '__main__':
    main()
