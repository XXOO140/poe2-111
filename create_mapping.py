#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import json

mapping = {}

# 基础符文
basic_runes = {
    'desert rune': '沙漠符文',
    'glacial rune': '冰川符文',
    'storm rune': '风暴符文',
    'iron rune': '锻铁符文',
    'body rune': '肉体符文',
    'mind rune': '心灵符文',
    'rebirth rune': '重生符文',
    'inspiration rune': '启发符文',
    'stone rune': '岩石符文',
    'vision rune': '远见符文',
    'adept rune': '行家符文',
    'robust rune': '坚实符文',
    'resolve rune': '坚毅符文',
    'ward rune': '保护符文',
    'charging rune': '充能符文',
}

prefixes = {'lesser ': '次级', 'greater ': '高级', 'perfect ': '完美'}

for en, cn in basic_runes.items():
    mapping[en] = cn
    for prefix, cn_prefix in prefixes.items():
        mapping[prefix + en] = cn_prefix + cn

# 特殊符文
special = {
    'greater rune of leadership': '高级领袖符文',
    'greater rune of tithing': '高级奉纳符文',
    'greater rune of alacrity': '高级迅捷符文',
    'greater rune of nobility': '高级尊贵符文',
    'masterwork rune': '大师符文',
}
mapping.update(special)

# 传奇符文
legendary = {
    "hedgewitch assandra's rune of wisdom": '女巫阿桑德拉的智慧符文',
    "saqawal's rune of the sky": '萨奇沃的苍空符文',
    "fenumus' rune of agony": '费努姆斯的痛苦符文',
    "farrul's rune of grace": '法瑞尔的优雅符文',
    "farrul's rune of the chase": '法瑞尔的追逐符文',
    "craiceann's rune of warding": '克林斯昂的守护符文',
    "saqawal's rune of memory": '萨奇沃的回忆符文',
    "saqawal's rune of erosion": '萨奇沃的碎砾符文',
    "farrul's rune of the hunt": '法瑞尔的狩猎符文',
    "craiceann's rune of recovery": '克林斯昂的回复符文',
    "courtesan mannan's rune of cruelty": '交际花曼南的残酷符文',
    "thane grannell's rune of mastery": '格兰内尔领主的掌控符文',
    "fenumus' rune of spinning": '费努姆斯的飞旋符文',
    "countess seske's rune of archery": '塞丝克伯爵夫人的箭术符文',
    "thane girt's rune of wildness": '吉尔特领主的荒野符文',
    "fenumus' rune of draining": '费努姆斯的流失符文',
    "thane myrk's rune of summer": '默克领主的仲夏符文',
    "lady hestra's rune of winter": '赫丝特拉女士的寒冬符文',
    "thane leld's rune of spring": '雷尔德领主的春光符文',
    "the greatwolf's rune of claws": '巨狼之利爪符文',
    "the greatwolf's rune of willpower": '巨狼之意志符文',
}
mapping.update(legendary)

# 崇敬符文
veneration = {
    'rune of vitality': '崇敬活力符文',
    'rune of the hunt': '崇敬狩猎符文',
    'rune of acrobatics': '崇敬巧技符文',
    'rune of culmination': '崇敬终点符文',
    'rune of renown': '崇敬声望符文',
    'rune of accumulation': '崇敬积累符文',
    'rune of foundations': '崇敬基准符文',
    'rune of the prism': '崇敬棱镜符文',
    'rune of the blossom': '崇敬精通符文',
    'rune of consistency': '崇敬连贯符文',
    'rune of reach': '崇敬触及符文',
    'rune of vital flame': '崇敬生命火焰符文',
    'rune of confrontation': '崇敬对峙符文',
}
mapping.update(veneration)

# 保护结界符文
warding = {
    'warding rune of reinforcement': '增援保护符文',
    'warding rune of protection': '护佑保护符文',
    'warding rune of disintegration': '崩解保护符文',
    'warding rune of desperation': '绝望保护符文',
    'warding rune of symbiosis': '共生保护符文',
    'warding rune of courage': '勇气保护符文',
    'warding rune of stability': '稳定保护符文',
    'warding rune of glancing': '斜擦保护符文',
    'warding rune of heart': '心源保护符文',
    'warding rune of nourishment': '滋养保护符文',
    'warding rune of annihilation': '歼灭保护符文',
    'warding rune of armature': '铠甲保护符文',
    'warding rune of obsession': '执念保护符文',
    'warding rune of equinox': '均衡保护符文',
    'warding rune of salvaging': '救援保护符文',
    'warding rune of bodyguards': '护卫保护符文',
    'warding rune of hollowing': '空蚀保护符文',
}
mapping.update(warding)

# 远古符文
ancient = {
    'ancient rune of splinters': '远古分裂符文',
    'ancient rune of dueling': '远古决斗符文',
    'ancient rune of the titan': '远古泰坦符文',
    'ancient rune of shattering': '远古粉碎符文',
    'ancient rune of prowess': '远古卓越符文',
    'ancient rune of control': '远古控制符文',
    'ancient rune of discovery': '远古探索符文',
    'ancient rune of decay': '远古腐朽符文',
    'ancient rune of witchcraft': '远古巫术符文',
    'ancient rune of the horde': '远古部族符文',
    'ancient rune of animosity': '远古敌意符文',
    'ancient rune of detonation': '远古引爆符文',
    'ancient rune of retaliation': '远古复仇符文',
}
mapping.update(ancient)

# 阿德爾系列
aldur = {
    "passion of aldur": '奥杜尔之热情',
    "breath of aldur": '奥杜尔之呼吸',
    "ire of aldur": '奥杜尔之忿怒',
    "betrayal of aldur": '奥杜尔之背叛',
    "aldur's legacy": '奥杜尔的遗产',
}
mapping.update(aldur)

# 其他传奇物品
other = {
    "astrid's creativity": '阿斯特丽德的创造',
    "cadigan's epiphany": '卡迪甘的顿悟',
    "serle's triumph": '瑟尔的凯旋',
    "uhtred's sidereus": '乌崔德的星辰',
    "kolr's hunt": '克尔的狩猎',
    "vorana's carnage": '沃拉娜的屠戮',
    "thrud's might": '斯鲁德的神力',
    "medved's tending": '梅德维德的照料',
    "katla's gloom": '卡塔拉的阴霾',
}
mapping.update(other)

# 保存
with open('src/PoeAncientsPriceHelper/item_names_cn.json', 'w', encoding='utf-8') as f:
    json.dump(mapping, f, ensure_ascii=False, indent=2)

print(f'创建了 {len(mapping)} 个映射')
