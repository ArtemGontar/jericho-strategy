#!/usr/bin/env python3
"""Generate polished 16:9 JERICHO interface drafts from shipped artwork."""

from html import escape
from pathlib import Path
import os
import shutil
import subprocess


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Documentation/Concepts/UI_Drafts"
ART = ROOT / "Assets/Game/Runtime/Client/Resources/Art"
MAP_SOURCE = Path("/workspace/generated_images/exec-a1ce1bd6-96eb-42b4-bf23-6b11cd6cb1f8.png")
W, H = 1920, 1080

# Charcoal, parchment, and aged brass. Surfaces stay neutral so the artwork leads.
INK = "#111714"
SURFACE = "#18201c"
SURFACE_2 = "#222b25"
PAPER = "#f1eadc"
DIM = "#c1c2b8"
MUTED = "#929b90"
GOLD = "#d1ab62"
SAGE = "#829887"
RUST = "#a96956"
GREEN = "#87a17d"


def esc(value):
    return escape(str(value), quote=True)


def rect(x, y, w, h, fill=SURFACE, radius=10, opacity=1, stroke=None, stroke_width=1):
    border = f' stroke="{stroke}" stroke-width="{stroke_width}"' if stroke else ""
    return (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{radius}" '
            f'fill="{fill}" opacity="{opacity}"{border}/>')


def line(x1, y1, x2, y2, color="#a7a38d", opacity=.30, width=1):
    return f'<path d="M{x1} {y1} L{x2} {y2}" fill="none" stroke="{color}" stroke-width="{width}" opacity="{opacity}"/>'


def text(x, y, value, size=22, color=PAPER, weight=400, anchor="start", opacity=1, letter_spacing=0):
    return (f'<text x="{x}" y="{y}" font-family="DejaVu Sans, sans-serif" font-size="{size}" '
            f'font-weight="{weight}" text-anchor="{anchor}" fill="{color}" opacity="{opacity}" '
            f'letter-spacing="{letter_spacing}">{esc(value)}</text>')


def multiline(x, y, values, size=20, color=PAPER, weight=400, step=None, anchor="start"):
    step = step or round(size * 1.45)
    result = [f'<text x="{x}" y="{y}" font-family="DejaVu Sans, sans-serif" font-size="{size}" '
              f'font-weight="{weight}" text-anchor="{anchor}" fill="{color}">']
    for i, value in enumerate(values):
        result.append(f'<tspan x="{x}" dy="{0 if i == 0 else step}">{esc(value)}</tspan>')
    result.append("</text>")
    return "".join(result)


def href(path):
    return Path(os.path.relpath(Path(path).resolve(), OUT.resolve())).as_posix()


def art(name):
    return href(ART / name)


def image(x, y, w, h, source, fit="xMidYMid slice", opacity=1):
    return f'<image x="{x}" y="{y}" width="{w}" height="{h}" preserveAspectRatio="{fit}" opacity="{opacity}" href="{source}"/>'


def button(x, y, w, label, primary=False, h=54, size=18, align="middle"):
    if primary:
        return (rect(x, y, w, h, GOLD, 8, .96) +
                text(x + w / 2, y + h / 2 + size * .34, label, size, INK, 750, "middle", letter_spacing=.3))
    return (line(x, y + h, x + w, y + h, PAPER, .17) +
            text(x + (w / 2 if align == "middle" else 15), y + h / 2 + size * .34, label,
                 size, PAPER, 650, "middle" if align == "middle" else "start", .92))


def chip(x, y, w, label, selected=False, h=43, size=15):
    fill = "#485b4f" if selected else SURFACE
    stroke = GOLD if selected else "#a9ad9e"
    return (rect(x, y, w, h, fill, 8, .88, stroke, 1 if selected else .65) +
            text(x + w / 2, y + h / 2 + size * .34, label, size, GOLD if selected else PAPER,
                 700 if selected else 500, "middle"))


def section(x, y, w, title, body=None, color=GOLD, size=19):
    body = body or []
    out = [text(x, y, title, size, color, 700), line(x, y + 15, x + w, y + 15, PAPER, .24)]
    if body:
        out.append(multiline(x, y + 51, body, 18, PAPER, 400, 30))
    return "".join(out)


def base(background=None, map_art=False, full_shade=.35, left_shade=True):
    out = ['<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 1920 1080" width="1920" height="1080">']
    out.append('<defs><linearGradient id="globalShade" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#101512" stop-opacity=".14"/><stop offset="1" stop-color="#101512" stop-opacity=".52"/></linearGradient><linearGradient id="leftShade" x1="0" x2="1"><stop offset="0" stop-color="#101512" stop-opacity=".88"/><stop offset=".58" stop-color="#101512" stop-opacity=".53"/><stop offset="1" stop-color="#101512" stop-opacity="0"/></linearGradient><linearGradient id="sideShade" x1="0" x2="1"><stop offset="0" stop-color="#101512" stop-opacity=".76"/><stop offset="1" stop-color="#101512" stop-opacity=".57"/></linearGradient></defs>')
    out.append(rect(0, 0, W, H, INK, 0))
    if background:
        out.append(image(0, 0, W, H, art("Backgrounds/" + background)))
        out.append('<rect width="1920" height="1080" fill="url(#globalShade)"/>')
    if map_art:
        out.append(image(0, 0, W, H, "map-art.png"))
        out.append('<rect width="1920" height="1080" fill="#101512" opacity=".30"/>')
    if left_shade:
        out.append('<rect width="1120" height="1080" fill="url(#leftShade)"/>')
    return "".join(out)


def finish(svg):
    return svg


def save(name, svg):
    (OUT / f"{name}.svg").write_text(svg + "</svg>", encoding="utf-8")


def header(title, section_name=None, back=None):
    out = []
    if section_name:
        out.append(text(64, 49, section_name.upper(), 14, SAGE, 700, letter_spacing=1.8))
    out.append(text(64, 103, title, 38, PAPER, 700))
    if back:
        prefix = "" if back.upper() in ("ЗАКРЫТЬ", "В МЕНЮ") else "←  "
        out.append(button(1635, 45, 220, prefix + back.upper(), False, 48, 15))
    return "".join(out)


def menu():
    s = finish(base("Menu_Background.png", left_shade=True))
    s += image(70, 150, 92, 92, art("Jericho_Logo.png"), "xMidYMid meet")
    s += text(177, 222, "JERICHO", 58, GOLD, 800, letter_spacing=1.2)
    s += text(78, 289, "От первого поселения — к цивилизации,", 19, PAPER, 500)
    s += text(78, 320, "которую выбирают строить.", 19, PAPER, 500)
    s += button(75, 382, 430, "НОВАЯ КАМПАНИЯ", True, 66, 20)
    s += button(75, 465, 430, "ПРОДОЛЖИТЬ · ХОД 24", False, 58, 18)
    s += line(75, 558, 500, 558, PAPER, .24)
    s += text(82, 610, "НАСТРОЙКИ", 16, PAPER, 650, letter_spacing=.5)
    s += text(260, 610, "КАК ИГРАТЬ", 16, PAPER, 650, letter_spacing=.5)
    s += text(82, 661, "ЭНЦИКЛОПЕДИЯ", 16, PAPER, 650, letter_spacing=.5)
    s += text(260, 661, "ДОСТИЖЕНИЯ", 16, PAPER, 650, letter_spacing=.5)
    s += text(78, 928, "ХРАНИТЕЛЬ ХРОНИК   ·   8 / 11 НАГРАД   ·   240 СЛАВЫ", 14, DIM, 600, letter_spacing=.4)
    s += text(78, 966, "ОДИНОЧНАЯ СТРАТЕГИЯ   ·   4 ДРЕВНИЕ КУЛЬТУРЫ", 13, MUTED, 500, letter_spacing=.7)
    save("01-main-menu", s)


NATIONS = [
    ("ИЕРИХОН", "Люди источника", "Башня источника", "здоровье поселений", "Leaders/Leader_Jericho.png", True),
    ("ШУМЕР", "Память табличек", "Зиккуратовый архив", "выпуск знаний", "Leaders/Leader_Sumer.png", False),
    ("ЕГИПЕТ", "Ритм разлива", "Двор нилометра", "вода и урожай", "Leaders/Leader_Egypt.png", False),
    ("ИНД", "Города воды", "Большая купальня", "здоровье и доверие", "Leaders/Leader_Indus.png", False),
]


def campaign_setup():
    s = finish(base("Campaign_Background.png", left_shade=False, full_shade=.5))
    s += '<rect width="1920" height="1080" fill="#101512" opacity=".34"/>'
    s += header("Новая кампания", "ВЫБОР ЦИВИЛИЗАЦИИ", "В МЕНЮ")
    card_y, card_h, card_w, gap = 160, 286, 426, 26
    for i, (name, epithet, district, bonus, portrait, selected) in enumerate(NATIONS):
        x = 54 + i * (card_w + gap)
        if i:
            s += line(x - gap / 2, card_y + 8, x - gap / 2, card_y + card_h - 6, PAPER, .17)
        s += image(x + 18, card_y + 18, 108, 118, art(portrait), "xMidYMid meet")
        s += text(x + 145, card_y + 51, name, 22, GOLD if selected else PAPER, 750)
        s += text(x + 145, card_y + 83, epithet, 15, DIM, 400)
        s += line(x + 20, card_y + 153, x + card_w - 20, card_y + 153, PAPER, .24)
        s += text(x + 22, card_y + 190, "УНИКАЛЬНЫЙ РАЙОН", 12, SAGE, 700, letter_spacing=1)
        s += text(x + 22, card_y + 222, district, 19, PAPER, 650)
        s += text(x + 22, card_y + 255, bonus, 15, DIM, 450)
        if selected:
            s += text(x + card_w - 27, card_y + 42, "✓", 22, GOLD, 700, "middle")
        s += line(x + 8, card_y + card_h, x + card_w - 8, card_y + card_h, GOLD if selected else PAPER,
                  .88 if selected else .16, 3 if selected else 1)
    s += text(65, 509, "ХРАНИТЕЛЬНИЦА ИСТОЧНИКА", 18, GOLD, 700, letter_spacing=.6)
    s += text(65, 540, "Собирательный образ старейшины · учебные районы дают +1 культурный престиж", 16, DIM, 450)
    s += line(65, 568, 1850, 568, PAPER, .22)
    s += text(65, 611, "СЛОЖНОСТЬ", 13, SAGE, 700, letter_spacing=1)
    s += chip(65, 628, 254, "СТАНДАРТНАЯ  ▾", True, 48, 16)
    s += text(371, 611, "ТЕМП ПАРТИИ", 13, SAGE, 700, letter_spacing=1)
    s += chip(371, 628, 254, "СТАНДАРТНЫЙ · 80 ХОДОВ  ▾", False, 48, 14)
    s += text(680, 642, "Карта большая   ·   4 соперника   ·   победа любым путём", 16, DIM, 450)
    s += button(1484, 610, 374, "НАЧАТЬ ПАРТИЮ", True, 64, 19)
    s += text(66, 722, "У каждой культуры — свой лидер, учебный центр, уникальный район и путь развития.", 15, DIM, 400)
    save("02-campaign-setup", s)


def slider(x, y, w, value, label):
    out = [text(x, y, label, 17, PAPER, 550), text(x + w, y, value, 16, GOLD, 700, "end")]
    out.append(line(x, y + 34, x + w, y + 34, "#a7ad9e", .55, 3))
    knob_x = x + w * (int(value.rstrip("%")) / 100)
    out.append(f'<circle cx="{knob_x}" cy="{y + 34}" r="8" fill="{GOLD}"/>')
    return "".join(out)


def toggle(x, y, label, enabled=True):
    out = [text(x, y + 24, label, 17, PAPER, 550)]
    fill = SAGE if enabled else "#59625a"
    out.append(rect(x + 452, y, 66, 32, fill, 16, .90))
    out.append(f'<circle cx="{x + (501 if enabled else 469)}" cy="{y + 16}" r="11" fill="{PAPER}"/>')
    return "".join(out)


def settings():
    s = finish(base("Settings_Background.png", left_shade=False))
    s += '<rect width="1920" height="1080" fill="#101512" opacity=".57"/>'
    s += header("Настройки", "ПЕРСОНАЛИЗАЦИЯ", "НАЗАД")
    s += text(75, 196, "ЗВУК", 15, SAGE, 700, letter_spacing=1.6)
    s += text(1000, 196, "ИЗОБРАЖЕНИЕ И ДОСТУПНОСТЬ", 15, SAGE, 700, letter_spacing=1.6)
    s += line(75, 215, 900, 215, PAPER, .28)
    s += line(1000, 215, 1836, 215, PAPER, .28)
    s += toggle(76, 249, "Музыка", True)
    s += slider(76, 314, 520, "38%", "Громкость музыки")
    s += toggle(76, 401, "Звуковые эффекты", True)
    s += slider(76, 465, 520, "28%", "Громкость эффектов")
    s += text(76, 566, "ВИБРООТКЛИК", 13, MUTED, 700, letter_spacing=1)
    s += chip(76, 583, 138, "ВЫКЛ.") + chip(225, 583, 138, "ВКЛЮЧЁН", True)
    s += text(1000, 256, "Анимации", 17, PAPER, 550)
    s += chip(1280, 230, 146, "ВЫКЛ.") + chip(1436, 230, 170, "БЫСТРЫЕ", True) + chip(1616, 230, 220, "ПОЛНЫЕ")
    s += line(1000, 302, 1836, 302, PAPER, .16)
    s += text(1000, 355, "Размер текста", 17, PAPER, 550)
    for i, label in enumerate(["90%", "100%", "115%", "130%"]):
        s += chip(1280 + i * 140, 329, 128, label, i == 1)
    s += line(1000, 401, 1836, 401, PAPER, .16)
    s += toggle(1000, 440, "Высокий контраст", False)
    s += line(1000, 500, 1836, 500, PAPER, .16)
    s += text(1000, 554, "Альбомная ориентация  ·  безопасная область iPhone", 14, DIM, 400)
    s += line(75, 665, 1836, 665, PAPER, .22)
    s += text(77, 714, "Настройки применяются сразу", 14, DIM, 450)
    s += text(77, 755, "СБРОСИТЬ ДОСТУПНОСТЬ", 14, SAGE, 700, letter_spacing=.6)
    save("03-settings", s)


def tutorial():
    s = finish(base("Tutorial_Background.png", left_shade=False))
    s += '<rect width="1920" height="1080" fill="#101512" opacity=".45"/>'
    s += header("Как играть", "ОБУЧЕНИЕ · 3 / 10", "ЗАКРЫТЬ")
    s += line(74, 150, 74, 916, GOLD, .38, 2)
    s += text(104, 207, "НАУКА", 14, SAGE, 700, letter_spacing=1.4)
    s += text(104, 270, "Выберите исследование", 35, PAPER, 700)
    s += multiline(104, 325, ["Знания открывают новые районы и помогают городу", "справляться с голодом, болезнями и кризисами."], 21, DIM, 400, 34)
    s += section(104, 446, 1070, "ЦЕЛЬ", ["Изучите технологию, которая поможет вашей столице."], GOLD, 17)
    s += text(104, 590, "Сначала откройте вкладку «Наука». Выберите доступную технологию.", 17, DIM, 400)
    s += button(104, 784, 280, "ПРЕДЫДУЩИЙ УРОК", False, 55, 14)
    s += button(405, 784, 255, "ПРОПУСТИТЬ", False, 55, 15)
    s += button(947, 784, 255, "ДАЛЕЕ  →", True, 55, 17)
    s += text(1320, 185, "ПУТЬ ИГРОКА", 14, SAGE, 700, letter_spacing=1.4)
    for i, label in enumerate(["Город", "Работники", "Исследование", "Проект", "Земля", "Поселенцы", "Соседи", "События", "Победа", "Летопись"]):
        y = 242 + i * 57
        active = i == 2
        s += f'<circle cx="1334" cy="{y - 6}" r="6" fill="{GOLD if active else SAGE if i < 2 else "#747d72"}"/>'
        s += text(1355, y, f"{i + 1:02d}  {label}", 16, PAPER if active else DIM, 700 if active else 450)
        if i < 9:
            s += line(1334, y + 7, 1334, y + 44, PAPER, .23, 1.4)
    s += text(1320, 880, "ЦЕЛЬ ВЫПОЛНЕНА?", 13, GOLD, 700, letter_spacing=.8)
    s += text(1320, 911, "Откройте нужную вкладку", 14, DIM, 450)
    save("04-tutorial", s)


GAME_PAGES = [
    ("05-world", "МИР", "Стратегическая карта", "Overview", [("ДЕРЖАВА", ["3 города · 4 соседа", "24,5 тыс. жителей · казна +8,2 / ход"]), ("НА ЭТОМ ХОДУ", ["Завершить рынок в столице", "Изучить ирригацию"]), ("КРИЗИС", ["Засуха · угроза 42", "Не хватает 8 еды до цели"])]),
    ("06-city", "ГОРОД", "Иерихон", "City", [("ГОРОДСКОЙ ПРОГНОЗ", ["Население 8,4 / 12 тыс.", "Еда +3,1 · здоровье 74 · доверие 68"]), ("РАБОТНИКИ", ["Земледельцы 3   −   4   +", "Ремесленники 2   −   3   +", "Специалисты 1   −   1   +"]), ("СТРОИТЕЛЬСТВО", ["Рынок · готовность 62%", "Следом: архивный район"])]),
    ("07-research", "НАУКА", "Исследования", "Research", [("В РАБОТЕ", ["Ирригация · 34 / 60", "Осталось 2 хода · фермы и каналы"]), ("ДОСТУПНО", ["Письменность · 40 знаний", "Медицина · 50 знаний", "Обработка бронзы · 55 знаний"]), ("СОСЕДИ", ["Шумер изучает письменность", "Египет развивает ирригацию"])]),
    ("08-diplomacy", "ДИПЛОМАТИЯ", "Соседи и договоры", "Diplomacy", [("ОТНОШЕНИЯ", ["Шумер · 68 · мир", "Египет · 42 · напряжение", "Инд · 73 · договор"]), ("ШУМЕР", ["Сила сопоставима · мир", "Торговый путь: ещё 4 хода"]), ("ДЕЙСТВИЕ", ["Торговля · обмен знаниями", "Гарантия · посредничество"])]),
    ("09-laws", "ЗАКОНЫ", "Уклад и институты", "Laws", [("ДЕЙСТВУЕТ", ["Собрание общин · ещё 6 ходов", "Содержание 2,4 казны / ход"]), ("РЕФОРМЫ", ["Совет городов · влияние", "Общая мера · доверие", "Писцовая служба · знания"]), ("СЛЕДУЮЩИЙ ШАГ", ["Новая реформа через 2 хода"])]),
    ("10-events", "СОБЫТИЯ", "Засуха в долине", "Events", [("УГРОЗА · 42 / 100", ["Пострадали два города", "Ответ нужен до конца хода"]), ("ВАРИАНТЫ", ["Отправить еду · цена казны", "Помочь знаниями · +слава", "Отказаться · риск оттока жителей"]), ("ЕЩЁ ДВА СОБЫТИЯ", ["Шумер просит посредничества", "В столице спор о налоге"])]),
    ("11-victory", "ПОБЕДА", "Пути к наследию", "Victory", [("БЛАГОПОЛУЧИЕ", ["74% · устойчивый рост", "Здоровье 78 · доверие 68"]), ("НАУКА И КУЛЬТУРА", ["Наука 48 / 100 · 2 из 3", "Культура 61 / 100 · 3 из 5"]), ("ПЛАН", ["Научный прорыв · 11 ходов", "Еды хватит на 5 ходов"])]),
    ("12-chronicle", "ЛЕТОПИСЬ", "Последствия хода 24", "Chronicle", [("ИТОГИ", ["Еда +3,1 · жители +0,4", "Казна +8,2 · знания +6"]), ("СОБЫТИЯ", ["Рынок завершён в столице", "Шумер продлил торговый путь", "Угроза засухи снизилась на 4"]), ("ФИЛЬТР", ["Все события · города · соседи"])]),
]


def game_screen(page):
    name, tab, title, _, sections = page
    s = finish(base(map_art=True, left_shade=False))
    s += rect(0, 0, 1920, 102, INK, 0, .88)
    s += text(38, 42, "JERICHO", 22, GOLD, 800, letter_spacing=1.2)
    s += text(38, 74, "ХОД 24  ·  ЭПОХА ГОРОДОВ", 13, DIM, 550, letter_spacing=.5)
    resources = [("КАЗНА", "+8,2"), ("ЕДА", "126"), ("ЗНАНИЯ", "34"), ("ВЛИЯНИЕ", "18"), ("КУЛЬТУРА", "61")]
    for i, (label, value) in enumerate(resources):
        x = 690 + i * 160
        s += text(x, 37, label, 11, MUTED, 700, "middle", letter_spacing=1)
        s += text(x, 70, value, 20, PAPER, 650, "middle")
    s += text(1818, 50, "ШУМЕР   ·   ЕГИПЕТ   ·   ИНД", 12, DIM, 550, "end")
    # The map is the primary surface; controls float over its edges.
    s += chip(54, 152, 182, "ВЛАДЕНИЯ", True, 42, 14)
    s += chip(246, 152, 112, "ЕДА", False, 42, 14)
    s += chip(368, 152, 198, "ПРОИЗВОДСТВО", False, 42, 14)
    s += chip(576, 152, 144, "ОБОРОНА", False, 42, 14)
    s += text(59, 858, "−     МАСШТАБ  100%     +", 13, PAPER, 600)
    s += '<rect x="1350" y="132" width="542" height="760" fill="url(#sideShade)"/>'
    s += line(1331, 132, 1331, 892, GOLD, .28, 1)
    s += text(1390, 193, title, 29, PAPER, 700)
    y = 252
    for heading, body in sections:
        s += section(1390, y, 455, heading, body, GOLD, 15)
        y += 102 + 30 * (len(body) - 1)
    s += text(1390, 846, "ПОЧЕМУ ТАК?  →  СПРАВОЧНИК", 13, SAGE, 700, letter_spacing=.4)
    s += rect(0, 918, 1920, 72, INK, 0, .68)
    nav = ["МИР", "ГОРОД", "НАУКА", "ДИПЛОМАТИЯ", "ЗАКОНЫ", "СОБЫТИЯ", "ПОБЕДА", "ЛЕТОПИСЬ"]
    for i, label in enumerate(nav):
        x = 28 + i * 235
        if label == tab:
            s += line(x, 920, x + 200, 920, GOLD, .95, 3)
        s += text(x + 100, 965, label, 14, GOLD if label == tab else DIM, 700 if label == tab else 500, "middle", letter_spacing=.4)
    s += rect(0, 991, 1920, 89, INK, 0, .78)
    s += text(40, 1043, "МЕНЮ", 14, DIM, 650)
    s += text(210, 1043, "СОХРАНИТЬ", 14, DIM, 650)
    s += text(410, 1043, "СПРАВКА", 14, DIM, 650)
    s += button(1574, 1008, 296, "ЗАВЕРШИТЬ ХОД  →", True, 54, 16)
    save(name, s)


def encyclopedia_index():
    s = finish(base("Archive_Background.png", left_shade=False))
    s += '<rect width="1920" height="1080" fill="#111714" opacity=".55"/>'
    s += header("Справочник", "ЭНЦИКЛОПЕДИЯ", "НАЗАД")
    s += rect(64, 141, 1070, 54, PAPER, 6, .96)
    s += text(88, 176, "⌕   Найти правило или событие…", 17, "#4f5e55", 400)
    for i, label in enumerate(["ВСЁ", "ГОРОДА", "ЭКОНОМИКА", "ДИПЛОМАТИЯ", "ПОБЕДА"]):
        s += chip(64 + i * 210, 215, 194, label, i == 0, 42, 13)
    entries = [
        ("Первые ходы", "Как начать развитие без риска", "СТАРТ"),
        ("Голод и миграция", "Почему жители покидают город", "ЭКОНОМИКА"),
        ("Города и районы", "Рост, специализация и районы", "ГОРОДА"),
        ("Дипломатия", "Договоры, доверие и маршруты", "СОСЕДИ"),
        ("Цена войны", "Мобилизация и восстановление", "КОНФЛИКТ"),
        ("Пути победы", "Планируйте наследие на годы вперёд", "ПОБЕДА"),
    ]
    for i, (title, description, tag) in enumerate(entries):
        row, col = divmod(i, 3)
        x, y = 68 + col * 606, 330 + row * 270
        s += text(x, y, tag, 12, SAGE, 700, letter_spacing=1.2)
        s += text(x, y + 45, title, 25, GOLD, 700)
        s += text(x, y + 83, description, 16, DIM, 400)
        s += text(x, y + 127, "ЧИТАТЬ  →", 14, PAPER, 650, letter_spacing=.4)
        s += line(x, y + 151, x + 525, y + 151, PAPER, .23)
        if col < 2:
            s += line(x + 555, y - 10, x + 555, y + 170, PAPER, .17)
    save("13-encyclopedia-index", s)


def encyclopedia_article():
    s = finish(base("Archive_Background.png", left_shade=False))
    s += '<rect width="1920" height="1080" fill="#111714" opacity=".67"/>'
    s += header("Почему жители уезжают", "СПРАВОЧНИК · ЭКОНОМИКА", "К СПИСКУ")
    s += text(70, 180, "Отток жителей начинается постепенно. Это ранний сигнал, что городу не хватает устойчивости.", 19, DIM, 400)
    s += section(72, 265, 800, "ЧТО ВЫЗЫВАЕТ ОТТОК", ["Нехватка еды и рабочих мест", "Здоровье или доверие долго остаются низкими", "Военные расходы разрушают торговлю и быт"], RUST, 17)
    s += section(1010, 265, 800, "КАК ВЕРНУТЬ УСТОЙЧИВОСТЬ", ["Сверьте запасы с прогнозом потребления", "Переведите часть жителей в земледелие", "Восстановите районы и торговые связи"], GREEN, 17)
    s += line(72, 570, 1810, 570, PAPER, .22)
    s += text(72, 621, "СВЯЗАННЫЕ ТЕМЫ", 13, SAGE, 700, letter_spacing=1)
    s += text(72, 666, "Городская устойчивость     ·     Продовольствие     ·     Военная нагрузка", 17, PAPER, 450)
    save("14-encyclopedia-article", s)


ACHIEVEMENTS = [
    ("Первый летописный лист", "Завершите первый ход", 20), ("Преобразить землю", "Создайте 5 улучшений", 30),
    ("Новые основания", "Основайте второй город", 35), ("Круг знаний", "Изучите 8 технологий", 40),
    ("Сеть доверия", "Удерживайте 2 мирных договора", 40), ("Города для людей", "Здоровье и доверие ≥70 в 2 городах", 35),
    ("Опора мира", "Помогите решить глобальный кризис", 30), ("Сложить мечи", "Завершите конфликт и восстановитесь", 45),
    ("Создатель наследия", "Победите в кампании", 100), ("Без меча", "Победите без объявления войны", 80),
    ("Голос культуры", "Завершите кампанию культурной победой", 60),
]


def achievements():
    s = finish(base("Victory_Background.png", left_shade=False))
    s += '<rect width="1920" height="1080" fill="#111714" opacity=".65"/>'
    s += header("Достижения", "ХРАНИТЕЛЬ ХРОНИК", "НАЗАД")
    s += text(68, 149, "Хранитель хроник   ·   240 славы   ·   8 / 11", 16, DIM, 450)
    for i, (title, desc, renown) in enumerate(ACHIEVEMENTS):
        row, col = divmod(i, 4)
        x, y = 68 + col * 455, 205 + row * 245
        if i % 4:
            s += line(x - 25, y, x - 25, y + 190, PAPER, .16)
        unlocked = i in (0, 1, 2, 3, 4, 6, 8, 9)
        s += f'<circle cx="{x + 24}" cy="{y + 28}" r="20" fill="{GOLD if unlocked else SURFACE_2}" opacity=".92"/>'
        s += text(x + 24, y + 35, "✦" if unlocked else "·", 18, INK if unlocked else MUTED, 800, "middle")
        s += text(x + 57, y + 35, title, 17, GOLD if unlocked else PAPER, 700)
        s += text(x, y + 82, desc, 14, DIM, 400)
        s += text(x, y + 119, ("ОТКРЫТО" if unlocked else "В ПРОГРЕССЕ") + f"   ·   +{renown} СЛАВЫ", 12, GREEN if unlocked else MUTED, 700, letter_spacing=.5)
        s += line(x, y + 151, x + 410, y + 151, PAPER, .21)
        s += text(x, y + 183, "ТИТУЛ ВЫБРАН" if i == 0 else "ЭКИПИРОВАТЬ ТИТУЛ" if unlocked else "", 12, SAGE, 650)
    s += text(72, 1013, "НАГРАДЫ МЕНЯЮТ ТОЛЬКО ПРОФИЛЬ И НЕ ВЛИЯЮТ НА БАЛАНС ПАРТИИ", 12, MUTED, 500, letter_spacing=.4)
    save("15-achievements", s)


SAVE_SLOTS = [
    ("РУЧНОЙ СЛОТ 1", "ХОД 24  ·  ШУМЕР", "СЕГОДНЯ · 14:32", True, True),
    ("РУЧНОЙ СЛОТ 2", "ХОД 18  ·  ИЕРИХОН", "ВЧЕРА · 20:14", True, True),
    ("РУЧНОЙ СЛОТ 3", "СВОБОДНЫЙ СЛОТ", "Можно сохранить кампанию", False, True),
    ("АВТОСОХРАНЕНИЕ · ПОСЛЕДНЕЕ", "ХОД 24  ·  ШУМЕР", "СЕГОДНЯ · 14:31", True, False),
    ("АВТОСОХРАНЕНИЕ · ПРЕДЫДУЩЕЕ", "ХОД 23  ·  ШУМЕР", "СЕГОДНЯ · 14:05", True, False),
    ("АВТОСОХРАНЕНИЕ · РАННЕЕ", "ХОД 22  ·  ШУМЕР", "СЕГОДНЯ · 13:42", True, False),
]


def save_manager():
    s = finish(base("Archive_Background.png", left_shade=False))
    s += '<rect width="1920" height="1080" fill="#111714" opacity=".68"/>'
    s += header("Сохранения", "КАМПАНИЯ · ХОД 24", "НАЗАД")
    for i, (label, detail, date, populated, manual) in enumerate(SAVE_SLOTS):
        row, col = divmod(i, 3)
        x, y = 64 + col * 610, 175 + row * 398
        s += rect(x, y, 568, 345, SURFACE, 8, .47, "#a99f83", .38)
        s += text(x + 25, y + 42, label, 13, SAGE, 700, letter_spacing=.8)
        s += line(x + 25, y + 59, x + 540, y + 59, PAPER, .20)
        s += text(x + 25, y + 115, detail, 21, PAPER if populated else DIM, 700)
        s += text(x + 25, y + 151, date, 14, MUTED, 450)
        s += line(x + 25, y + 190, x + 540, y + 190, PAPER, .18)
        if populated:
            s += button(x + 25, y + 230, 230, "ЗАГРУЗИТЬ", True, 49, 15)
        else:
            s += text(x + 25, y + 262, "Нет кампании в этом слоте", 15, MUTED, 450)
        if manual:
            s += button(x + 275, y + 230, 265, "СОХРАНИТЬ СЮДА", False, 49, 14)
    save("16-save-manager", s)


def modal(name, title, kicker, sections, background=None, map_background=False, actions=("ПРОДОЛЖИТЬ", "ЗАКРЫТЬ")):
    s = finish(base(background, map_art=map_background, left_shade=False))
    s += '<rect width="1920" height="1080" fill="#080d0b" opacity=".63"/>'
    heights = [72 + 30 * len(body) for _, body in sections]
    panel_h = min(810, max(490, 190 + sum(heights)))
    top = (1080 - panel_h) / 2
    s += rect(330, top, 1260, panel_h, "#151c18", 12, .82, "#bea562", .7)
    s += text(960, top + 59, title, 34, GOLD, 750, "middle")
    if kicker:
        s += text(960, top + 94, kicker.upper(), 12, SAGE, 700, "middle", letter_spacing=1.2)
    y = top + 153
    for heading, body in sections:
        s += section(405, y, 1110, heading, body, PAPER, 16)
        y += 62 + max(1, len(body)) * 30
    action_y = top + panel_h - 77
    s += button(405, action_y, 352, actions[0], True, 48, 16)
    s += button(780, action_y, 352, actions[1], False, 48, 16)
    if len(actions) > 2:
        s += text(1510, action_y + 31, actions[2], 14, DIM, 550, "end")
    save(name, s)


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    if MAP_SOURCE.exists():
        shutil.copy2(MAP_SOURCE, OUT / "map-art.png")
    menu()
    campaign_setup()
    settings()
    tutorial()
    for page in GAME_PAGES:
        game_screen(page)
    encyclopedia_index()
    encyclopedia_article()
    achievements()
    save_manager()
    modal("17-hex-selection", "Плодородный луг", "КЛЕТКА · 1 ОЧКО ОТ СТОЛИЦЫ", [
        ("РЕСУРС", ["Плодородная почва · +2 еды", "Земля принадлежит Иерихону"]),
        ("ДЕЙСТВИЯ", ["Назначить работника", "Построить ферму · 12 производства", "Проложить дорогу · 8 производства"]),
    ], map_background=True, actions=("ПОСТРОИТЬ ФЕРМУ", "ЗАКРЫТЬ", "НАЗНАЧИТЬ ЖИТЕЛЯ"))
    modal("18-turn-resolution", "Ход 24 завершён", "ИТОГИ ЦИВИЛИЗАЦИИ", [
        ("ВЫПУСК", ["Еда +3,1   ·   казна +8,2   ·   знания +6,0"]),
        ("СОБЫТИЯ", ["Рынок построен в столице", "Шумер продлил торговый путь", "Угроза засухи снизилась на 4"]),
    ], map_background=True, actions=("СЛЕДУЮЩИЙ ХОД", "К ЛЕТОПИСИ"))
    modal("19-battle-report", "Столкновение у переправы", "ВОЕННЫЙ ОТЧЁТ", [
        ("СИЛЫ", ["Ваш отряд · ополчение · сила 46", "Соперник · полевые войска · сила 58"]),
        ("ПОСЛЕДСТВИЯ", ["Потери с обеих сторон", "Торговый путь прерван", "Военная нагрузка давит на города"]),
    ], "Diplomacy_Background.png", actions=("ПРИНЯТЬ ИТОГ", "ОТВЕТИТЬ ДИПЛОМАТИЕЙ"))
    modal("20-era-transition", "Эпоха городов", "НОВАЯ ЭПОХА", [
        ("ОТКРЫТИЯ", ["Школы, рынки и новые институты", "Районы получают новую архитектуру", "Соседи замечают рост вашего влияния"]),
    ], "City_Background.png", actions=("ПРОДОЛЖИТЬ", "ПОДРОБНЕЕ"))
    modal("21-campaign-result", "Наследие создано", "ПОБЕДА · КУЛЬТУРА", [
        ("ИТОГИ", ["Ход 74 / 120   ·   население 86", "Знания 91   ·   доверие 78", "Открыт титул «Меценат» · +60 славы"]),
        ("ДАЛЬШЕ", ["Разобрать летопись", "Поделиться историей", "Начать новую партию"]),
    ], "Victory_Background.png", actions=("ЛЕТОПИСЬ", "НОВАЯ ПАРТИЯ", "НАГРАДЫ"))
    modal("22-campaign-defeat", "Летопись продолжится", "СОПЕРНИК ДОСТИГ ЦЕЛИ", [
        ("ИТОГИ", ["Ход 68 / 80", "Ваши города сохранили жителей и культурные районы"]),
        ("ДАЛЬШЕ", ["Разобрать решающие ходы", "Посмотреть достижения", "Начать другую партию"]),
    ], "Defeat_Background.png", actions=("РАЗОБРАТЬ ХОДЫ", "НОВАЯ ПАРТИЯ", "НАГРАДЫ"))
    create_boards()


def create_boards():
    screens = sorted(OUT.glob("[0-9][0-9]-*.svg"))
    for old in OUT.glob("Board_*.png"):
        old.unlink()
    for old in OUT.glob("[0-9][0-9]-*.png"):
        old.unlink()
    groups = [screens[i:i + 4] for i in range(0, len(screens), 4)]
    for index, group in enumerate(groups, 1):
        height = 1080 if len(group) > 2 else 540
        pieces = [f'<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 1920 {height}" width="1920" height="{height}">', rect(0, 0, 1920, height, INK, 0)]
        for i, screen in enumerate(group):
            col, row = i % 2, i // 2
            pieces.append(f'<svg x="{col * 960}" y="{row * 540}" width="960" height="540" viewBox="0 0 1920 1080" preserveAspectRatio="xMidYMid meet"><image x="0" y="0" width="1920" height="1080" href="{screen.name}"/></svg>')
        pieces.append("</svg>")
        board = OUT / f"Board_{index:02d}.svg"
        board.write_text("".join(pieces), encoding="utf-8")
        png = OUT / f"Board_{index:02d}.png"
        subprocess.run(["inkscape", str(board), "--export-type=png", f"--export-filename={png}", "--export-width=1920"], check=True, stdout=subprocess.DEVNULL)
        jpg = OUT / f"Board_{index:02d}.jpg"
        subprocess.run(["magick", str(png), "-quality", "88", str(jpg)], check=True, stdout=subprocess.DEVNULL)
        png.unlink()
    for screen in screens:
        png = screen.with_suffix(".png")
        subprocess.run(["inkscape", str(screen), "--export-type=png", f"--export-filename={png}", "--export-width=1440"], check=True, stdout=subprocess.DEVNULL)
        jpg = screen.with_suffix(".jpg")
        subprocess.run(["magick", str(png), "-quality", "88", str(jpg)], check=True, stdout=subprocess.DEVNULL)
        png.unlink()


if __name__ == "__main__":
    build()
