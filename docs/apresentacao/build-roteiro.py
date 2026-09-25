"""Builds the speaker's script for the Agent Squad talk."""

import sys
from pathlib import Path

from docx import Document
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor

# --- palette ----------------------------------------------------------------

INK = RGBColor(0x1A, 0x1D, 0x22)
GREY = RGBColor(0x5B, 0x63, 0x6E)
BLUE = RGBColor(0x1F, 0x5F, 0xBF)
GREEN = RGBColor(0x1B, 0x7F, 0x3B)
AMBER = RGBColor(0x9A, 0x6B, 0x06)
RED = RGBColor(0xB3, 0x2D, 0x2D)
VIOLET = RGBColor(0x6B, 0x3F, 0xA0)

SHADE_CODE = "F2F4F7"
SHADE_WARN = "FFF6E5"
SHADE_TIP = "EAF3FF"
SHADE_HEAD = "1F5FBF"

doc = Document()

# --- page + base styles -----------------------------------------------------

for section in doc.sections:
    section.top_margin = Cm(2.0)
    section.bottom_margin = Cm(2.0)
    section.left_margin = Cm(2.2)
    section.right_margin = Cm(2.2)

normal = doc.styles["Normal"]
normal.font.name = "Segoe UI"
normal.font.size = Pt(10.5)
normal.font.color.rgb = INK
normal.paragraph_format.space_after = Pt(7)
normal.paragraph_format.line_spacing = 1.22

# Idioma do documento. Sem isto o leitor de tela lê português com fonética de inglês, o que
# é a diferença entre um documento utilizável e um que dá dor de cabeça em dez minutos.
_lang = normal.element.get_or_add_rPr().makeelement(qn("w:lang"), {})
_lang.set(qn("w:val"), "pt-BR")
normal.element.get_or_add_rPr().append(_lang)

doc.core_properties.title = "Roteiro de apresentação — Agent Squad — TDC São Paulo 2026"
doc.core_properties.author = "Alexandre Costa"
doc.core_properties.language = "pt-BR"


def shade(paragraph, color):
    pPr = paragraph._p.get_or_add_pPr()
    shd = OxmlElement("w:shd")
    shd.set(qn("w:val"), "clear")
    shd.set(qn("w:fill"), color)
    pPr.append(shd)


def border_left(paragraph, color):
    pPr = paragraph._p.get_or_add_pPr()
    borders = OxmlElement("w:pBdr")
    left = OxmlElement("w:left")
    left.set(qn("w:val"), "single")
    left.set(qn("w:sz"), "18")
    left.set(qn("w:space"), "8")
    left.set(qn("w:color"), color)
    borders.append(left)
    pPr.append(borders)


def h1(text, color=BLUE):
    # Estilo "Heading 1" de verdade, não negrito grande: é o que alimenta o painel de
    # navegação do Word e o atalho de pular por títulos do leitor de tela. A aparência
    # continua a mesma porque a formatação vai no run, que vence o estilo.
    p = doc.add_paragraph(style="Heading 1")
    p.paragraph_format.space_before = Pt(22)
    p.paragraph_format.space_after = Pt(8)
    p.paragraph_format.keep_with_next = True
    r = p.add_run(text)
    r.font.size = Pt(19)
    r.font.bold = True
    r.font.color.rgb = color
    return p


def h2(text, color=INK):
    p = doc.add_paragraph(style="Heading 2")
    p.paragraph_format.space_before = Pt(15)
    p.paragraph_format.space_after = Pt(5)
    p.paragraph_format.keep_with_next = True
    r = p.add_run(text)
    r.font.size = Pt(13.5)
    r.font.bold = True
    r.font.color.rgb = color
    return p


def para(text, size=10.5, color=INK, bold=False, italic=False, after=7, before=0, indent=0):
    p = doc.add_paragraph()
    p.paragraph_format.space_after = Pt(after)
    p.paragraph_format.space_before = Pt(before)
    if indent:
        p.paragraph_format.left_indent = Cm(indent)
    r = p.add_run(text)
    r.font.size = Pt(size)
    r.font.color.rgb = color
    r.font.bold = bold
    r.font.italic = italic
    return p


def rich(parts, size=10.5, after=7, indent=0):
    p = doc.add_paragraph()
    p.paragraph_format.space_after = Pt(after)
    if indent:
        p.paragraph_format.left_indent = Cm(indent)
    for part in parts:
        text, color = part[0], part[1]
        bold = part[2] if len(part) > 2 else False
        mono = part[3] if len(part) > 3 else False
        r = p.add_run(text)
        r.font.size = Pt(size if not mono else size - 0.5)
        r.font.color.rgb = color
        r.font.bold = bold
        if mono:
            r.font.name = "Consolas"
    return p


def bullet(text, color=INK, bold_lead=None, indent=0.6):
    p = doc.add_paragraph()
    p.paragraph_format.left_indent = Cm(indent)
    p.paragraph_format.space_after = Pt(4)
    marker = p.add_run("•  ")
    marker.font.color.rgb = BLUE
    marker.font.bold = True
    if bold_lead:
        r = p.add_run(bold_lead)
        r.font.bold = True
        r.font.color.rgb = INK
        r.font.size = Pt(10.5)
    r = p.add_run(text)
    r.font.color.rgb = color
    r.font.size = Pt(10.5)
    return p


def code(lines, shade_color=SHADE_CODE):
    for index, line in enumerate(lines):
        p = doc.add_paragraph()
        p.paragraph_format.left_indent = Cm(0.5)
        p.paragraph_format.space_after = Pt(0 if index < len(lines) - 1 else 9)
        p.paragraph_format.space_before = Pt(7 if index == 0 else 0)
        p.paragraph_format.line_spacing = 1.05
        shade(p, shade_color)
        r = p.add_run(line if line else " ")
        r.font.name = "Consolas"
        r.font.size = Pt(9.5)
        r.font.color.rgb = INK


def callout(title, text, color=AMBER, fill=SHADE_WARN):
    p = doc.add_paragraph()
    p.paragraph_format.space_before = Pt(9)
    p.paragraph_format.space_after = Pt(0)
    p.paragraph_format.left_indent = Cm(0.2)
    shade(p, fill)
    border_left(p, f"{color.__str__()}")
    r = p.add_run(title)
    r.font.bold = True
    r.font.size = Pt(10.5)
    r.font.color.rgb = color

    p = doc.add_paragraph()
    p.paragraph_format.space_before = Pt(0)
    p.paragraph_format.space_after = Pt(10)
    p.paragraph_format.left_indent = Cm(0.2)
    shade(p, fill)
    border_left(p, f"{color.__str__()}")
    r = p.add_run(text)
    r.font.size = Pt(10.5)
    r.font.color.rgb = INK


def table(headers, rows, widths=None):
    t = doc.add_table(rows=1, cols=len(headers))
    t.style = "Table Grid"
    t.alignment = WD_TABLE_ALIGNMENT.CENTER

    # Marca a primeira linha como cabeçalho de verdade: o leitor de tela passa a anunciar
    # "Coluna: Min" antes de cada célula em vez de despejar números soltos, e a linha se
    # repete quando a tabela quebra de página.
    tr_pr = t.rows[0]._tr.get_or_add_trPr()
    tr_pr.append(tr_pr.makeelement(qn("w:tblHeader"), {}))

    for index, header in enumerate(headers):
        cell = t.rows[0].cells[index]
        cell.text = ""
        p = cell.paragraphs[0]
        p.paragraph_format.space_after = Pt(2)
        p.paragraph_format.space_before = Pt(2)
        shade(p, SHADE_HEAD)
        r = p.add_run(header)
        r.font.bold = True
        r.font.size = Pt(9.5)
        r.font.color.rgb = RGBColor(0xFF, 0xFF, 0xFF)

    for row in rows:
        cells = t.add_row().cells
        for index, value in enumerate(row):
            cells[index].text = ""
            p = cells[index].paragraphs[0]
            p.paragraph_format.space_after = Pt(2)
            p.paragraph_format.space_before = Pt(2)
            r = p.add_run(str(value))
            r.font.size = Pt(9.5)
            r.font.color.rgb = INK

    if widths:
        for row in t.rows:
            for index, width in enumerate(widths):
                row.cells[index].width = Cm(width)

    doc.add_paragraph().paragraph_format.space_after = Pt(4)
    return t


def slides_tag(text):
    rich([("SLIDES  ", BLUE, True), (text, GREY, False, True)], size=9.5, after=6)


# ===========================================================================
# Cover
# ===========================================================================
p = doc.add_paragraph()
p.paragraph_format.space_before = Pt(40)
p.paragraph_format.space_after = Pt(4)
r = p.add_run("Agent Squad")
r.font.size = Pt(34)
r.font.bold = True
r.font.color.rgb = BLUE

para("Roteiro de apresentação — TDC São Paulo 2026", size=16, color=INK, after=2)
para("Uma hora · quatro demos ao vivo · 35 slides", size=12, color=GREY, after=22)

rich([("Apresentador: ", GREY), ("Alexandre Costa", INK, True)], size=11, after=3)
rich([("Repositório: ", GREY), ("github.com/magoolation/tdc-sp-2026-agent-squad", BLUE, True, True)], size=11, after=3)
rich([("Deck: ", GREY), ("Agent-Squad-TDC-SP-2026.pptx", INK, False, True)], size=11, after=24)

callout(
    "O slide 2 está pronto",
    "Nome, cargo, bio e perfis preenchidos, sem foto — como você pediu. Não sobrou marcador em "
    "nenhum slide. Se quiser mexer em alguma palavra da bio, é edição direta no PowerPoint; "
    "o resto do deck não depende dela.",
    color=GREEN, fill="EAF7EE",
)

callout(
    "Como usar este roteiro",
    "As falas aqui não são para decorar nem ler. São as batidas de cada bloco: a ideia que tem que passar, "
    "a frase que vale repetir, e o que fazer com as mãos enquanto o agente pensa. "
    "Leia uma vez inteiro hoje de manhã, e leve impressa só a linha do tempo e a seção de contingência.",
    color=BLUE, fill=SHADE_TIP,
)

doc.add_page_break()

# ===========================================================================
# Checklist
# ===========================================================================
h1("1. Checklist antes do palco")

h2("T-2h — na sua máquina")
code([
    "cd E:\\source\\repos\\TDCSP2026",
    "",
    "# 1. Tudo verde, incluindo chamada real aos modelos e ao Copilot",
    "dotnet run --project src/AgentSquad.Cli -- doctor --probe-models --probe-copilot",
    "",
    "# 2. Limpe execuções anteriores",
    "dotnet build-server shutdown",
    "Remove-Item -Recurse -Force C:\\squad\\wt\\*, C:\\squad\\bld\\* -ErrorAction SilentlyContinue",
    "",
    "# 3. Aqueça o build — ninguém quer ver restore no palco",
    "dotnet build AgentSquad.slnx -c Release",
])

callout(
    "Se o --probe-models ou o --probe-copilot falhar, pare tudo e resolva",
    "Não suba no palco com um deles vermelho. O probe do Copilot leva 24 segundos e exercita exatamente "
    "o caminho que a demo 3 depende. É a diferença entre descobrir o problema agora e descobrir na frente de trezentas pessoas.",
    color=RED, fill="FDECEC",
)

h2("T-30min — a sala")
bullet("Terminal em fonte 18pt ou maior, tema escuro, janela larga. As tabelas do Spectre quebram feio em janela estreita.")
bullet("Desligue notificações do sistema, do Teams e do Slack.")
bullet("Resolução do projetor testada — rode o doctor uma vez nela para conferir que as cores aparecem.")
bullet("Bateria no carregador. O laptop vai rodar cinco agentes em paralelo.")

h2("Abas do navegador, já abertas e logadas")
bullet("O repositório alvo, na aba Issues")
bullet("O repositório alvo, na aba Pull requests")
bullet("Um PR de um ensaio anterior, já aberto e rolado até o corpo")
bullet("Portal do Foundry (ai.azure.com), no projeto")
bullet("Uma aba em branco para o dashboard do Aspire")

h2("Plano B carregado")
para("Deixe uma execução COMPLETA de ensaio já pronta no repositório, com issues e PRs abertos. "
     "Se a rede do evento cair — e a rede do evento cai — você navega nela e a palestra continua inteira. "
     "Anote aqui o número dos PRs do ensaio:", after=6)
code(["PRs do ensaio:  #____   #____   #____   #____   #____"])

doc.add_page_break()

# ===========================================================================
# Timeline
# ===========================================================================
h1("2. Linha do tempo")

para("Os tempos são alvos, não trilhos. Se atrasar, o lugar para cortar está marcado na seção 4 — "
     "e nunca é a seção de achados nem a de limitações.", color=GREY, after=10)

table(
    ["Min", "Bloco", "Slides", "O que acontece"],
    [
        ("0–2", "Abertura", "1–2", "Título e quem é você"),
        ("2–3", "Agenda", "3", "Promessa: metade é demo, e os achados são inéditos"),
        ("3–8", "O problema", "4–5", "Por que paralelizar agentes é diferente"),
        ("8–16", "A fábrica", "6–10", "Pipeline, stack, agentes, as duas decisões"),
        ("16–19", "DEMO 1", "11", "squad doctor"),
        ("19–24", "Personalização", "12–13", "Regras como arquivo versionado"),
        ("24–31", "DEMO 2", "14–15", "Reunião → requisitos"),
        ("31–35", "HITL e plano", "16–17", "Perguntar bem; validador vs crítico"),
        ("35–43", "DEMO 3", "18–19", "Cinco agentes, três ondas, o gate"),
        ("43–46", "Segurança", "20", "Modelo de ameaça e sandbox"),
        ("46–49", "DEMO 4", "21", "Os pull requests"),
        ("49–57", "Os achados", "22–31", "Oito coisas que quebraram"),
        ("57–59", "Na sua empresa", "32–34", "Por onde começar; o que não negociar"),
        ("59–60", "Fecho", "35", "Quatro frases"),
        ("60–70", "Perguntas", "36", "Link no telão o tempo todo"),
    ],
    widths=[1.7, 3.4, 1.7, 9.4],
)

callout(
    "Sobre o tempo das demos",
    "A demo 3 é a mais longa e a mais arriscada: o planejamento leva de 6 a 8 minutos de relógio. "
    "NÃO fique em silêncio esperando. A seção 4 tem o que falar durante cada espera — use como texto de apoio.",
    color=AMBER,
)

doc.add_page_break()

# ===========================================================================
# Bloco a bloco
# ===========================================================================
h1("3. Bloco a bloco")

# --- abertura
h2("Abertura — slides 1 e 2 · 2 min")
slides_tag("1–2")
para("Abra pelo que a sessão NÃO é. Boa parte da plateia já usa Copilot ou Claude Code todo dia; "
     "se você prometer 'um agente que escreve código', perde a sala nos primeiros trinta segundos.", after=6)
rich([("Fala de abertura: ", GREY, True),
      ("\"Isso aqui não é sobre um agente escrever código — vocês já viram isso. É sobre colocar cinco "
       "agentes para trabalhar ao mesmo tempo sem que eles briguem entre si, e sobre quem decide se o "
       "que eles fizeram presta.\"", INK)], after=8)
para("No slide 2, trinta segundos. Uma coisa que te dá credibilidade para falar disso — não o currículo.", color=GREY, after=6)

# --- agenda
h2("Agenda — slide 3 · 1 min")
slides_tag("3")
para("Não leia item por item. Aponte e diga duas coisas: metade do tempo é demo ao vivo, "
     "e o bloco de achados é o que você não encontra em blog post. Prometa — e depois entregue.", after=6)

# --- problema
h2("O problema — slides 4 e 5 · 5 min")
slides_tag("4–5")
para("Slide 4 desarma a expectativa. Slide 5 é a fundação da palestra — vá devagar.", after=6)
bullet("Peça um levantar de mãos: quem já teve dois PRs conflitando por falta de coordenação HUMANA?", indent=0.6)
bullet("Então: agora imagine isso automatizado, cinco vezes, sem ninguém olhando.", indent=0.6)
rich([("Frase para fechar o bloco: ", GREY, True),
      ("\"Nenhum desses problemas se resolve com um modelo melhor. Todos se resolvem com restrição.\"", INK, True)], after=8)

# --- fábrica
h2("A fábrica — slides 6 a 10 · 8 min")
slides_tag("6–10")
para("Slide 6 (pipeline): percorra os oito blocos apontando, em 90 segundos. Não detalhe — cada demo abre um deles. "
     "O que precisa ficar são os dois portões amarelos.", after=6)
rich([("Fala: ", GREY, True),
      ("\"A fábrica é autônoma no meio e amarrada nas pontas. É exatamente isso que a torna utilizável "
       "numa empresa de verdade.\"", INK)], after=8)
para("Slides 7 e 8 (stack e agentes): 60 segundos cada, rápido. Destaque só 'sem nenhuma chave de API' "
     "e 'seis agentes pensam, um escreve'.", after=6)
para("Slides 9 e 10 são os mais importantes da palestra. Duas frases para repetir devagar:", after=6)
rich([("1. ", BLUE, True), ("\"O modelo produz. O programa verifica.\"", INK, True)], after=3, indent=0.6)
rich([("2. ", BLUE, True), ("\"Dois work items da mesma onda nunca declaram o mesmo arquivo — e isso é "
                            "verificado por código, não confiado ao modelo.\"", INK, True)], after=8, indent=0.6)

doc.add_page_break()

# --- demo 1
h2("DEMO 1 — pré-requisitos — slide 11 · 3 min")
slides_tag("11")
code([
    "dotnet run --project src/AgentSquad.Cli -- doctor --probe-models",
])
para("O que apontar enquanto roda (leva ~40 s por causa do gpt-5.5):", after=5)
bullet("Não é checagem de configuração — é chamada REAL a cada modelo, com resposta e latência.", indent=0.6)
bullet("Cada falha traz o comando exato que corrige. Compare mentalmente com 'algo deu errado'.", indent=0.6)
bullet("Repare no gpt-5.5 levando 37 segundos: modelo de raciocínio tem custo de latência. Isso vai voltar no achado 2.", indent=0.6)

# --- personalização
h2("Personalização — slides 12 e 13 · 5 min")
slides_tag("12–13")
para("Aqui a plateia corporativa acorda. A pergunta que todo tech lead faz é 'como eu faço ele seguir o NOSSO padrão?'.", after=6)
rich([("Resposta, dita assim: ", GREY, True),
      ("\"Não é fine-tuning e não é prompt secreto. É arquivo no repositório, que passa por code review "
       "como qualquer outro. Para adaptar à sua empresa você edita dois arquivos e nenhuma linha de C#.\"", INK)], after=8)
para("Se o tempo permitir, mostre ao vivo — são 15 segundos e impressiona:", after=5)
code([
    "copilot instruction list",
    "copilot skill list",
])
para("O slide 13 fecha com o exemplo do apontamento citando SEC-004 com arquivo e linha. "
     "Diga: um revisor que não sabe nomear a regra que está aplicando está expressando gosto — "
     "e gosto não pode bloquear pipeline.", after=8)

# --- demo 2
h2("DEMO 2 — da reunião ao requisito — slides 14 e 15 · 7 min")
slides_tag("14–15")
code([
    "dotnet run --project src/AgentSquad.Cli -- run `",
    "  --transcript samples/meeting-transcripts/kickoff-catalogo.md `",
    "  --repo squad-demo-catalogo --plan-only",
])
callout(
    "Use --plan-only nesta demo",
    "Ela para depois do plano e não escreve nada no GitHub. Você pode rodar quantas vezes quiser, "
    "inclusive se algo der errado no palco.",
    color=BLUE, fill=SHADE_TIP,
)
para("Enquanto o agente lê a transcrição (~60 s), conte como o arquivo foi escrito:", after=5)
bullet("Um requisito que ninguém quantificou (\"rápido\", \"instantâneo\")", indent=0.6)
bullet("Uma divergência que ficou sem resolução — e é a mais perigosa, porque todo mundo saiu achando que estava combinado", indent=0.6)
bullet("Um \"obviamente\" que não é óbvio", indent=0.6)
bullet("Um escopo que cresceu no meio da conversa", indent=0.6)
bullet("Uma decisão tomada por quem chegou atrasado", indent=0.6)
para("Quando as perguntas aparecerem, PARE e leia uma em voz alta com as opções. "
     "O ponto não é a pergunta — é que cada opção mostra o impacto no plano.", after=6)
callout(
    "O momento que a plateia fotografa",
    "Slide 15. Se o agente listar o p95 como PENDÊNCIA, pare e deixe a sala ler. "
    "Se ele registrar como requisito acordado, pare também — e diga que é exatamente por isso que existe "
    "aprovação humana. Os dois desfechos servem à sua tese.",
    color=VIOLET, fill="F3EEFA",
)

doc.add_page_break()

# --- HITL e plano
h2("HITL e plano — slides 16 e 17 · 4 min")
slides_tag("16–17")
para("Slide 16: uma pergunta ruim é pior que nenhuma — consome atenção e não melhora a decisão. "
     "Dê o critério dos três testes: muda o plano? não dá para responder lendo o repositório? "
     "escolher errado é caro? Falhou em um, o agente decide e registra como suposição contestável.", after=6)
para("Slide 17: adiante um achado aqui, encaixa perfeito. Em três execuções o crítico NUNCA aprovou. "
     "A lição não é 'melhore o prompt' — é que você não pode dar poder de veto a algo que não sabe parar de opinar.", after=8)

# --- demo 3
h2("DEMO 3 — cinco agentes, três ondas — slides 18 e 19 · 8 min")
slides_tag("18–19")
code([
    "dotnet run --project src/AgentSquad.Cli -- run `",
    '  --request "Preciso de uma API REST de catálogo de produtos em .NET: busca paginada`',
    '             por nome, filtro por categoria e faixa de preço, com testes de integração." `',
    "  --repo squad-demo-catalogo --parallel 3",
])
callout(
    "Esta é a demo longa. Planeje a conversa.",
    "Intake ~40 s · requisitos ~90 s · planejamento 4 a 6 min · agentes 3 a 20 min por onda. "
    "Você NÃO vai esperar em silêncio. Use o tempo do planejamento para percorrer os slides 9, 10 e 17 de novo — "
    "as duas decisões e a hierarquia validador/crítico. A plateia entende muito melhor vendo o agente trabalhar "
    "enquanto você explica a regra.",
    color=AMBER,
)
callout(
    "Achado reserva, para a espera do planejamento",
    "Tem uma história de bastidor que cabe exatamente aqui e não tem slide, então você a conta olhando "
    "para a tela do planejamento. Horas antes desta palestra, uma execução classificou o repositório como "
    "\"existing .NET 8 catalog API\". O main dele tem dois arquivos. O que o intake leu foi a saída da "
    "execução ANTERIOR: o clone é reaproveitado entre execuções e tinha ficado no branch de integração da "
    "última. A primeira decisão do pipeline inteiro — greenfield ou brownfield — vinha de estado local que "
    "ninguém mergeou. O remate: \"o modelo estava certo; o que eu dei para ele ler é que estava errado\". "
    "Se preferir dar slide a ela, troque pelo achado 4 ou pelo 5 — são os mais específicos de Windows.",
    color=VIOLET, fill="F3EEFA",
)
para("Momentos para apontar, em ordem:", after=5)
bullet("\"O repositório estava vazio; commit inicial criado\" — o caminho greenfield, achado 6 em ação", indent=0.6)
bullet("As issues aparecendo: troque para o navegador. Elas estão lá de verdade, com labels de onda e tamanho", indent=0.6)
bullet("\"Onda 1/3: 1 agente\" → \"Onda 2/3: 2 agentes em paralelo\" — o paralelismo começando", indent=0.6)
bullet("Abra o dashboard do Aspire: spans GenAI ao vivo, com tokens e latência por chamada", indent=0.6)
callout(
    "Se o gate reprovar, COMEMORE",
    "É o melhor momento possível da palestra. Roteiro pronto: \"Repararam? O agente disse que terminou. "
    "O gate discordou. O diagnóstico do compilador volta para ele, ele corrige, e roda de novo. "
    "Num sistema autônomo, a única coisa em que você pode confiar é no que um programa verificou.\" "
    "Isso vende a ideia melhor do que qualquer slide.",
    color=GREEN, fill="EAF7EE",
)

# --- segurança
h2("Segurança — slide 20 · 3 min")
slides_tag("20")
para("Slide obrigatório para plateia corporativa — é a primeira pergunta do time de segurança. "
     "O ponto central, dito com clareza:", after=6)
rich([("\"A gente não PEDE ao agente para não dar push. A política de permissões IMPEDE, e negação vence "
       "aprovação automática. Isso é diferente de instrução em prompt — e numa auditoria a diferença é tudo.\"", INK)], after=8)

doc.add_page_break()

# --- demo 4
h2("DEMO 4 — os pull requests — slide 21 · 3 min")
slides_tag("21")
code([
    "gh pr list --repo magoolation/squad-demo-catalogo",
    "",
    "# depois abra um no navegador",
])
para("Abra UM pull request de item e role pelo corpo, apontando:", after=5)
bullet("A tabela do gate, com as sete etapas e a duração de cada uma", indent=0.6)
bullet("A revisão automatizada citando TST-004, com arquivo, linha e correção concreta", indent=0.6)
bullet("A seção \"Atenção do revisor\": riscos declarados e arquivos fora do escopo — nada escondido", indent=0.6)
bullet("O bloco de procedência: execução, modelo, tentativas, duração, sessão do Copilot", indent=0.6)
para("Depois volte à lista e abra o PR de entrega — o que vai de agent/run-... para main:", after=5)
bullet("A tabela de entrega: cada issue, a área, se passou no gate e quantas tentativas levou", indent=0.6)
bullet("A seção \"O que não entrou\", quando houver — é o que separa relatório de propaganda", indent=0.6)
bullet("As decisões que a fábrica tomou sem perguntar, num bloco recolhível", indent=0.6)
bullet("A última linha do corpo: \"O merge é seu.\"", indent=0.6)
callout(
    "Explique os dois níveis antes que alguém pergunte",
    "Os PRs por item apontam para o branch de integração e servem para LER uma mudança de cada vez. "
    "O PR de entrega é a única coisa da execução que propõe mexer no main. Alguns PRs de item vão "
    "aparecer como MERGED — o GitHub faz isso sozinho quando os commits viram alcançáveis pela base. "
    "Diga isso ANTES de alguém apontar, e emende no achado 8. Em uma frase: "
    "\"revisar em pedaços, decidir de uma vez\".",
    color=VIOLET, fill="F3EEFA",
)

# --- achados
h2("Os achados — slides 22 a 31 · 8 min")
slides_tag("22–31")
para("Transição importante. Diga em voz alta:", after=5)
rich([("\"A partir daqui eu paro de te vender a solução e começo a te contar o que deu errado.\"", INK, True)], after=8)
para("Oito slides, ~50 segundos cada. Não se demore em nenhum — o valor está no conjunto. "
     "Se precisar cortar, corte o achado 4 (worktree) e o 5 (NuGet): são os mais específicos de Windows.", after=6)
callout(
    "O achado 6 é o melhor da palestra. Não corte.",
    "O gate reprovava o agente por um bug MEU no --artifacts-path. Rodei o gate à mão no código dele: "
    "passava inteiro, com seis testes. A frase: \"um gate que culpa a parte errada é pior do que gate nenhum — "
    "fez um agente correto parecer incompetente e queimou as tentativas de reparo consertando algo que "
    "nunca esteve errado.\"",
    color=VIOLET, fill="F3EEFA",
)
callout(
    "Os achados 7 e 8 são um par. Conte na ordem.",
    "O 7 é o problema: as ondas não se enxergavam, então criei um branch de integração. O 8 é o preço "
    "dessa correção: quando a integração avança, o GitHub marca os PRs daquela onda como MERGED sozinho — "
    "os commits deles viraram alcançáveis pela base. Três PRs \"mergeados\" sem revisão nenhuma. "
    "É comportamento normal de stacked PRs, não bug do GitHub, mas deixava a execução terminar sem nada "
    "para aprovar. A correção final: um PR de entrega, do branch de integração para main, em draft. "
    "A frase: \"os PRs por item são unidades de revisão; o PR de entrega é o portão\".",
    color=VIOLET, fill="F3EEFA",
)
para("Se alguém apontar os PRs MERGED na tela durante a demo 4, ótimo — é exatamente a deixa do achado 8. "
     "Diga: \"boa, você acabou de achar o meu oitavo achado\".", after=6)
para("Slide 31 é a tese da palestra em quatro linhas. Se alguém for embora depois disso, levou o essencial: "
     "construir uma fábrica de agentes é 20% prompt e 80% engenharia de integração e verificação.", after=8)

# --- empresa
h2("Na sua empresa — slides 32 a 34 · 3 min")
slides_tag("32–34")
para("Slide 32 é o mais prático da palestra e a plateia está esperando por ele. Enfatize o passo 2:", after=6)
rich([("\"Se o CI de vocês hoje não reprova um PR humano mal formatado, automatizar agente é prematuro. "
       "O gate é pré-requisito, não consequência.\"", INK, True)], after=8)
para("Slide 33: leia os seis não-negociáveis devagar. Cada um é uma conversa que a pessoa vai ter com "
     "segurança, com compliance ou com o diretor dela. Ter a resposta pronta é o que faz o projeto ser aprovado.", after=6)
callout(
    "Slide 34 — não corte por falta de tempo",
    "É o slide das limitações, e é o que separa a sua palestra das outras vinte sobre IA no evento. "
    "Plateia técnica perdoa limitação declarada e desconfia de solução perfeita. Se estiver atrasado, "
    "corte um achado, não este.",
    color=RED, fill="FDECEC",
)

# --- fecho
h2("Fecho — slide 35 · 1 min")
slides_tag("35")
para("Quatro frases, trinta segundos, com pausa entre elas. Depois passe direto para perguntas — "
     "não encerre duas vezes.", after=8)

doc.add_page_break()

# ===========================================================================
# Contingência
# ===========================================================================
h1("4. Quando der errado", color=RED)

para("Vai dar. Plateia técnica perdoa falha explicada e desconfia de demo perfeita demais. "
     "O que não se perdoa é você ficar mudo tentando consertar.", after=10)

table(
    ["Sintoma", "O que fazer, ao vivo"],
    [
        ("A rede caiu", "Passe para --plan-only, que não toca no GitHub. Se nem isso, navegue no ensaio anterior: as issues e PRs estão lá."),
        ("Foundry devolvendo 429", "Baixe --parallel para 1. Diga em voz alta que é rate limit e que o teto de concorrência existe para isso."),
        ("Um agente travou", "Ele estoura o timeout e a execução continua. FALE ISSO em voz alta — é o controle funcionando, não um defeito."),
        ("Copilot sem autenticação", "copilot login em outro terminal, ou exporte GH_TOKEN. Enquanto isso, mostre o PR do ensaio."),
        ("Worktree não some", "dotnet build-server shutdown e siga. A limpeza acontece no fim da execução."),
        ("O plano saiu ruim", "Use Revisar com um comentário. Mostrar o arquiteto corrigindo é uma das melhores demos que existem."),
        ("O gate reprovou", "Comemore. Use o roteiro do bloco da demo 3."),
        ("Demo travou de vez", "Ctrl+C, navegue no ensaio anterior, e siga para os achados. Você não perde nada da tese."),
    ],
    widths=[4.5, 11.7],
)

callout(
    "A regra de ouro",
    "Se algo quebrar, explique O QUE quebrou e POR QUÊ, em uma frase, e siga. "
    "Você está apresentando um sistema cuja tese central é que verificação importa porque as coisas falham. "
    "Uma falha ao vivo, bem explicada, é argumento a seu favor.",
    color=GREEN, fill="EAF7EE",
)

doc.add_page_break()

# ===========================================================================
# Perguntas
# ===========================================================================
h1("5. Perguntas prováveis")

para("Respostas curtas. Se você não sabe, diga que não sabe — é mais forte que improvisar.", color=GREY, after=10)

qa = [
    ("Quanto custa uma execução?",
     "Medi tempo, não dinheiro: 40 minutos com seis agentes envolvidos. Não instrumentei custo por execução, "
     "e isso está na lista de limitações. Seria a primeira coisa que eu mediria antes de usar isso em produção."),
    ("Funciona em repositório legado grande?",
     "Validei greenfield e mudanças pequenas. Em brownfield o intake lê o repositório antes de opinar, mas "
     "não testei em escala de centenas de milhares de linhas. Não vou dizer que funciona sem ter medido."),
    ("Isso substitui desenvolvedor?",
     "Não. Move o trabalho de escrever código para especificar e revisar — que é onde ele já estava indo. "
     "E o merge continua humano, por desenho, não por limitação técnica."),
    ("Por que não CrewAI, AutoGen ou LangGraph?",
     "A escolha de .NET e Agent Framework foi contextual. O desenho — gate determinístico, regra de conflito "
     "de arquivo, worktree isolado, dois portões humanos — é independente de framework. É o que eu levaria "
     "para qualquer stack."),
    ("E se o agente fizer besteira?",
     "A política de permissões impede push, abertura de PR e comandos de infraestrutura. Escrita confinada "
     "ao worktree, com verificação de path traversal testada. E nada entra sem merge humano."),
    ("Vi PRs marcados como MERGED. Quem aprovou aquilo?",
     "Ninguém — e essa é a resposta honesta. O GitHub marca um PR como merged quando os commits dele passam "
     "a ser alcançáveis pela base, e é isso que a integração entre ondas faz. Comportamento normal de stacked "
     "PRs. Por isso a execução termina abrindo um PR de entrega, do branch de integração para main, em draft: "
     "os PRs por item são unidades de revisão, o de entrega é o portão. Foi o meu achado número 8."),
    ("Por que não usou o grafo de workflows do Agent Framework?",
     "Usei o framework em tudo — agentes, sessões, saída estruturada, telemetria. Para a onda paralela "
     "escolhi C# explícito porque preciso de concorrência limitada, orçamento por agente, laço de reparo e "
     "limpeza de worktree garantida. Um grafo esconderia exatamente o controle que o operador mais precisa ver. "
     "Está documentado num ADR no repositório."),
    ("Quanto tempo levou para construir?",
     "Cheguei na primeira versão do produto em uma manhã. O resto do dia foi o que realmente consumiu "
     "tempo, e não foi prompt: foi integração — versão de pacote, permissão, layout de runtime, "
     "hierarquia de autoridade. Os oito achados da palestra saíram todos dessa parte, nenhum da manhã."),
    ("Posso usar no meu trabalho?",
     "MIT, está tudo no repositório: código, regras, runbook e a transcrição de exemplo."),
]

for question, answer in qa:
    rich([("P.  ", BLUE, True), (question, INK, True)], size=11, after=3)
    rich([("R.  ", GREY, True), (answer, INK)], size=10.5, after=11)

doc.add_page_break()

# ===========================================================================
# Anexo
# ===========================================================================
h1("6. Anexo — comandos")

h2("Os quatro comandos da apresentação")
code([
    "# DEMO 1",
    "dotnet run --project src/AgentSquad.Cli -- doctor --probe-models",
    "",
    "# DEMO 2",
    "dotnet run --project src/AgentSquad.Cli -- run `",
    "  --transcript samples/meeting-transcripts/kickoff-catalogo.md `",
    "  --repo squad-demo-catalogo --plan-only",
    "",
    "# DEMO 3",
    "dotnet run --project src/AgentSquad.Cli -- run `",
    '  --request "Preciso de uma API REST de catálogo de produtos em .NET..." `',
    "  --repo squad-demo-catalogo --parallel 3",
    "",
    "# DEMO 4",
    "gh pr list --repo magoolation/squad-demo-catalogo",
])

h2("Com a interface web e o dashboard do Aspire")
code([
    "dotnet run --project src/AgentSquad.AppHost",
])

h2("Limpeza entre ensaios")
code([
    "$r = 'magoolation/squad-demo-catalogo'",
    "",
    "gh pr list --repo $r --json number --jq '.[].number' | ForEach-Object {",
    "  gh pr close $_ --repo $r --delete-branch }",
    "",
    "gh issue list --repo $r --json number --jq '.[].number' | ForEach-Object {",
    "  gh issue close $_ --repo $r }",
    "",
    "dotnet build-server shutdown",
    "Remove-Item -Recurse -Force C:\\squad\\wt\\*, C:\\squad\\bld\\* -ErrorAction SilentlyContinue",
])

h2("Se precisar provisionar o Foundry de novo")
code([
    "az cognitiveservices usage list -l eastus2 -o table   # confira a cota ANTES",
    "",
    "az deployment group create -g rg-agent-squad -f infra/main.bicep `",
    "  -p principalId=$(az ad signed-in-user show --query id -o tsv) -p location=eastus2",
])

doc.add_paragraph()
rich([("Runbook completo, com mais detalhe operacional: ", GREY),
      ("docs/demo-runbook.md", BLUE, True, True)], size=10.5, after=4)
rich([("Boa palestra.", INK, True)], size=12, after=0)

out = Path(sys.argv[1] if len(sys.argv) > 1 else "Roteiro-Agent-Squad-TDC-SP-2026.docx")
out.parent.mkdir(parents=True, exist_ok=True)
doc.save(out)
print(f"ok: {out}")
