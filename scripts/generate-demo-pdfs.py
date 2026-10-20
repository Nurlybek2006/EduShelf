"""Create the six original Kazakh lesson sheets shipped with EduShelf.

Install reportlab first. Pass --font/--bold-font on non-Windows systems.
The source texts are original demonstration lessons, not external publications.
"""
from __future__ import annotations

import argparse
from pathlib import Path
from xml.sax.saxutils import escape

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle


LESSONS = [
    {
        "file": "algebra-linear-equations.pdf", "subject": "Математика", "level": "7-сынып",
        "title": "Сызықтық теңдеулер", "subtitle": "Белгісіз шаманы қадаммен табу",
        "goal": "Бір айнымалысы бар сызықтық теңдеуді шешу және жауабын орнына қойып тексеру.",
        "sections": [
            ("Негізгі ұғым", "Теңдеу - құрамында белгісіз саны бар теңдік. Теңдеудің түбірі деп теңдікті дұрыс ететін айнымалы мәнін айтады. ax + b = c түрінде a нөлге тең болмаса, x = (c - b) / a."),
            ("Шешу үлгісі", "3x + 5 = 20. Екі жақтан да 5-ті азайтамыз: 3x = 15. Екі жақты 3-ке бөлеміз: x = 5. Тексеру: 3 · 5 + 5 = 20. Демек, түбірі 5."),
            ("Өзің орында", "1. 2x + 7 = 19 теңдеуін шеш. 2. 5x - 4 = 21 теңдеуінің түбірін тап. 3. Бір санды 4-ке көбейтіп, 3-ті қосқанда 31 шығады. Сол санды тап."),
            ("Жауап пен түсіндіру", "1. x = 6, себебі 2 · 6 + 7 = 19. 2. x = 5. 3. 4x + 3 = 31, сондықтан x = 7. Әр қадамда теңдеудің екі жағына бірдей амал қолдан."),
        ],
        "check": "Неліктен теңдеудің тек бір жағын ғана өзгертуге болмайды? Өз мысалыңмен түсіндір.",
    },
    {
        "file": "geometry-triangle-area.pdf", "subject": "Математика", "level": "7-сынып",
        "title": "Үшбұрыштың ауданы", "subtitle": "Табан мен биіктіктің байланысы",
        "goal": "Үшбұрыш ауданын табан мен оған түсірілген биіктік арқылы есептеу.",
        "sections": [
            ("Негізгі ұғым", "Үшбұрыштың ауданы S = a · h / 2 формуласымен есептеледі. Мұнда a - таңдалған табанның ұзындығы, h - қарсы төбеден сол табан жатқан түзуге түсірілген перпендикулярдың ұзындығы."),
            ("Шешу үлгісі", "Табаны 8 см, оған түсірілген биіктігі 5 см үшбұрыш берілсін. S = 8 · 5 / 2 = 20 см². Аудан бірлігі - шаршы сантиметр. Биіктік міндетті түрде таңдалған табанға перпендикуляр болуы керек."),
            ("Өзің орында", "1. a = 12 см және h = 4 см болса, ауданды тап. 2. Ауданы 30 см², табаны 10 см үшбұрыштың биіктігін тап. 3. Табан өзгермей, биіктік екі есе артса, аудан қалай өзгереді?"),
            ("Жауап пен түсіндіру", "1. S = 24 см². 2. h = 2S / a = 60 / 10 = 6 см. 3. Аудан да екі есе артады, себебі формуладағы биіктік пен аудан тура пропорционал."),
        ],
        "check": "Үшбұрыштың кез келген қабырғасын табан етіп алуға бола ма? Биіктікті қалай таңдайсың?",
    },
    {
        "file": "physics-uniform-motion.pdf", "subject": "Физика", "level": "7-сынып",
        "title": "Бірқалыпты қозғалыс", "subtitle": "Жол, жылдамдық және уақыт",
        "goal": "Бірқалыпты қозғалыста s = v · t байланысын қолданып есеп шығару.",
        "sections": [
            ("Негізгі ұғым", "Дене кез келген тең уақыт аралығында бірдей жол жүрсе, қозғалыс бірқалыпты деп аталады. Жылдамдық v = s / t. Жол s = v · t. Уақыт t = s / v. SI жүйесінде жол метрмен, уақыт секундпен, жылдамдық м/с-пен өлшенеді."),
            ("Шешу үлгісі", "Велосипедші 4 м/с жылдамдықпен 30 с қозғалды. Жүрген жолы: s = 4 · 30 = 120 м. Егер жылдамдық км/сағ түрінде берілсе, м/с-ке ауыстыру үшін 3,6-ға бөлеміз: 18 км/сағ = 5 м/с."),
            ("Өзің орында", "1. Жаяу жүргінші 1,5 м/с жылдамдықпен 40 с жүрді. Жолын тап. 2. Дене 150 м жолды 30 с-та өтті. Орташа жылдамдығын есепте. 3. 72 км/сағ жылдамдықты м/с-ке айналдыр."),
            ("Жауап пен түсіндіру", "1. 60 м. 2. 5 м/с. 3. 20 м/с. Орташа жылдамдықты табу үшін бүкіл жолды бүкіл уақытқа бөлеміз; қозғалыс бірқалыпты болмаса да, осы анықтама қолданылады."),
        ],
        "check": "Есепті бастамас бұрын барлық шаманы бірдей өлшем бірліктеріне келтірдің бе?",
    },
    {
        "file": "informatics-algorithms.pdf", "subject": "Информатика", "level": "6-сынып",
        "title": "Алгоритм құру", "subtitle": "Мәселені нақты қадамдарға бөлу",
        "goal": "Сызықтық, тармақталу және қайталану құрылымдарын күнделікті мысалдардан ажырату.",
        "sections": [
            ("Негізгі ұғым", "Алгоритм - нәтижеге жеткізетін түсінікті әрі реттелген нұсқаулар тізбегі. Әр қадам орындаушыға анық болуы тиіс. Сызықтық алгоритмде қадамдар ретімен орындалады; тармақталуда шарт тексеріледі; циклде әрекет қайталанады."),
            ("Шешу үлгісі", "Екі санның үлкенін табу алгоритмі: 1. a және b сандарын енгіз. 2. Егер a > b болса, a санын шығар. 3. Әйтпесе b санын шығар. Екі сан тең болса да, шығарылған сан олардың үлкен мәніне тең болады."),
            ("Өзің орында", "1. Үш санның қосындысын табудың сызықтық алгоритмін жаз. 2. Санның жұп не тақ екенін анықтайтын шартты ата. 3. 1-ден 5-ке дейінгі сандарды шығару үшін қайталанатын әрекетті сипатта."),
            ("Жауап пен түсіндіру", "1. a, b, c енгіз; s = a + b + c есепте; s шығар. 2. Санды 2-ге бөлгендегі қалдық 0 болса, сан жұп. 3. i = 1 мәнінен баста; i-ді шығар; i-ге 1 қос; i ≤ 5 болғанша қайтала."),
        ],
        "check": "Алгоритміңді басқа адам қосымша сұрақ қоймай орындай ала ма? Қадамдарды тексер.",
    },
    {
        "file": "kazakh-text-main-idea.pdf", "subject": "Қазақ тілі", "level": "6-сынып",
        "title": "Мәтіннің негізгі ойы", "subtitle": "Тақырып пен автор ойын ажырату",
        "goal": "Қысқа мәтіннің тақырыбын анықтау, негізгі ойын бір сөйлеммен тұжырымдау.",
        "sections": [
            ("Негізгі ұғым", "Мәтіннің тақырыбы оның не туралы екенін білдіреді. Негізгі ойы автордың сол тақырып бойынша айтқысы келген басты пікірін көрсетеді. Тірек сөздер мәтінді түсінуге көмектеседі, бірақ негізгі ой толық сөйлеммен беріледі."),
            ("Оқуға арналған мәтін", "Айша күн сайын кітап оқуға он минут бөледі. Алғашында ол бірнеше бетті ғана оқитын. Бір айдан кейін жаңа сөздерді көбірек түсініп, өз ойын анық жеткізе бастады. Аз уақытты тұрақты пайдалану оның оқу дағдысын жақсартты."),
            ("Өзің орында", "1. Мәтінге қысқа тақырып қой. 2. Үш тірек сөзді белгіле. 3. Негізгі ойды бір сөйлеммен жаз. 4. Мәтіннен өз жауабыңды дәлелдейтін бір әрекетті ата."),
            ("Жауап үлгісі", "Тақырып: «Күнделікті оқу». Тірек сөздер: кітап, тұрақтылық, дағды. Негізгі ой: күн сайын аз уақыт бөлудің өзі оқу дағдысын дамытуға көмектеседі. Дәлел: Айша бір айдан соң жаңа сөздерді жақсырақ түсінді."),
        ],
        "check": "Сенің тұжырымың бүкіл мәтінді қамти ма, әлде тек бір бөлшегін қайталай ма?",
    },
    {
        "file": "biology-plant-cell.pdf", "subject": "Биология", "level": "7-сынып",
        "title": "Өсімдік жасушасы", "subtitle": "Құрылым мен қызметті байланыстыру",
        "goal": "Өсімдік жасушасының негізгі бөліктерін атап, олардың қызметін түсіндіру.",
        "sections": [
            ("Негізгі ұғым", "Жасуша - тірі ағзалардың құрылымдық және қызметтік бірлігі. Өсімдік жасушасының плазмалық мембранасы заттардың өтуін реттейді. Жасуша қабырғасы тірек береді. Цитоплазмада көптеген тіршілік үдерістері жүреді."),
            ("Бөліктердің қызметі", "Ядрода тұқымқуалаушылық ақпараттың басым бөлігі сақталады. Ірі орталық вакуоль су мен еріген заттарды жинақтап, жасушаның ішкі қысымын сақтауға қатысады. Хлоропласттарда жарық энергиясының көмегімен фотосинтез жүреді."),
            ("Өзің орында", "1. Жасуша қабырғасы мен мембрананың қызметін салыстыр. 2. Хлоропласттың қызметін ата. 3. «Өсімдіктің барлық жасушасында хлоропласт бар» деген пікірді бағала. Тамыр жасушаларын ескер."),
            ("Жауап пен түсіндіру", "1. Қабырға пішін мен тірек береді, мембрана заттардың өтуін реттейді. 2. Хлоропласт фотосинтезге қатысады. 3. Пікір дұрыс емес: жарық түспейтін тамырдың көптеген жасушасында хлоропласт болмайды."),
        ],
        "check": "Құрылымды тек жаттамай, оның қызметін мысалмен түсіндіре аласың ба?",
    },
]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "DemoFiles")
    parser.add_argument("--font", type=Path, default=Path("C:/Windows/Fonts/arial.ttf"))
    parser.add_argument("--bold-font", type=Path, default=Path("C:/Windows/Fonts/arialbd.ttf"))
    args = parser.parse_args()
    for font in (args.font, args.bold_font):
        if not font.is_file():
            parser.error(f"Font not found: {font}. Supply --font and --bold-font with Kazakh glyph support.")
    pdfmetrics.registerFont(TTFont("EduSans", str(args.font)))
    pdfmetrics.registerFont(TTFont("EduSansBold", str(args.bold_font)))
    pdfmetrics.registerFontFamily("EduSans", normal="EduSans", bold="EduSansBold")
    args.output.mkdir(parents=True, exist_ok=True)

    navy = colors.HexColor("#14263D")
    orange = colors.HexColor("#ED8138")
    muted = colors.HexColor("#617084")
    body = ParagraphStyle("body", fontName="EduSans", fontSize=10.3, leading=16, textColor=navy, spaceAfter=8)
    heading = ParagraphStyle("heading", parent=body, fontName="EduSansBold", fontSize=11.5, leading=16, spaceBefore=12, spaceAfter=5)
    title = ParagraphStyle("title", parent=body, fontName="EduSansBold", fontSize=25, leading=31, spaceAfter=9)
    subtitle = ParagraphStyle("subtitle", parent=body, fontSize=12, leading=18, textColor=muted, spaceAfter=22)
    small = ParagraphStyle("small", parent=body, fontSize=9, leading=14, spaceAfter=0)

    def page_frame(canvas, doc):
        canvas.saveState()
        width, height = A4
        canvas.setFillColor(navy)
        canvas.rect(0, height - 16, width, 16, fill=1, stroke=0)
        canvas.setFillColor(orange)
        canvas.rect(44, height - 52, 21, 5, fill=1, stroke=0)
        canvas.setFont("EduSansBold", 10)
        canvas.setFillColor(navy)
        canvas.drawString(75, height - 53, "EduShelf / Оқу парағы")
        canvas.setStrokeColor(colors.HexColor("#E5E8EC"))
        canvas.line(44, 47, width - 44, 47)
        canvas.setFont("EduSans", 8)
        canvas.setFillColor(muted)
        canvas.drawString(44, 32, "Демонстрациялық авторлық материал • Өз бетіңше оқуға арналған")
        canvas.drawRightString(width - 44, 32, str(doc.page))
        canvas.restoreState()

    for lesson in LESSONS:
        output = args.output / lesson["file"]
        doc = SimpleDocTemplate(str(output), pagesize=A4, leftMargin=44, rightMargin=44,
                                topMargin=85, bottomMargin=66, title=lesson["title"], author="EduShelf",
                                subject=lesson["subject"], pageCompression=1)
        story = [Paragraph(f'{lesson["subject"]}  /  {lesson["level"]}', small), Spacer(1, 13),
                 Paragraph(escape(lesson["title"]), title), Paragraph(escape(lesson["subtitle"]), subtitle)]
        box = Table([[Paragraph("<b>Оқу мақсаты</b><br/>" + escape(lesson["goal"]), body)]], colWidths=[A4[0] - 88])
        box.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), colors.HexColor("#F7F5F0")),
                                 ("BOX", (0, 0), (-1, -1), 0.5, colors.HexColor("#E5E8EC")),
                                 ("LEFTPADDING", (0, 0), (-1, -1), 14), ("RIGHTPADDING", (0, 0), (-1, -1), 14),
                                 ("TOPPADDING", (0, 0), (-1, -1), 12), ("BOTTOMPADDING", (0, 0), (-1, -1), 6)]))
        story += [box, Spacer(1, 4)]
        for label, content in lesson["sections"]:
            story += [Paragraph(escape(label), heading), Paragraph(escape(content), body)]
        story += [Paragraph("Өзіңді тексер", heading), Paragraph(escape(lesson["check"]), body)]
        doc.build(story, onFirstPage=page_frame, onLaterPages=page_frame)
        print(f"Created {output.name} ({output.stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
