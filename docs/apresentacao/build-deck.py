"""Builds the Agent Squad talk deck for TDC São Paulo 2026."""

import sys
from pathlib import Path

from pptx import Presentation
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import MSO_ANCHOR, PP_ALIGN
from pptx.util import Inches, Pt

sys.path.insert(0, str(Path(__file__).parent))

from theme import (  # noqa: E402
    ACCENT, ACCENT_DEEP, BG, BG_RAISED, BG_SUNKEN, BORDER, CONTENT_W, DANGER, DIM, FONT,
    H, MARGIN, MONO, MUTED, SUCCESS, TEXT, VIOLET, W, WARNING, WHITE,
    blank, box, bullets, chip, code_panel, heading, metric, notes, rich, rule, textbox, write,
)

prs = Presentation()
prs.slide_width = W
prs.slide_height = H

plain = []  # slides that get no footer


# ===========================================================================
# 1 — Title
# ===========================================================================
def slide_title():
    s = blank(prs)
    plain.append(s)

    band = box(s, 0, 0, Inches(0.16), H, fill=ACCENT_DEEP, outline=None, radius=False)

    f = textbox(s, Inches(1.2), Inches(1.9), Inches(11), Inches(0.5))
    write(f, "TDC SÃO PAULO 2026", size=15, color=ACCENT, bold=True, space_after=0, first=True)

    f = textbox(s, Inches(1.2), Inches(2.4), Inches(11.2), Inches(1.9))
    write(f, "Agent Squad", size=66, color=WHITE, bold=True, space_after=2, first=True)
    write(f, "Uma fábrica de software autônoma", size=32, color=TEXT, space_after=0)

    f = textbox(s, Inches(1.2), Inches(4.45), Inches(11), Inches(0.6))
    write(
        f,
        "Da transcrição de uma reunião ao pull request — com agentes em paralelo, "
        "e um humano decidindo o que importa.",
        size=19, color=MUTED, space_after=0, first=True,
    )

    rule(s, Inches(5.5), color=BORDER, left=Inches(1.2), width=Inches(10.9))

    f = textbox(s, Inches(1.2), Inches(5.75), Inches(11), Inches(0.5))
    rich(f, [
        (".NET 10", TEXT, True), ("   ·   ", DIM),
        ("Microsoft Agent Framework", TEXT, True), ("   ·   ", DIM),
        ("Microsoft Foundry", TEXT, True), ("   ·   ", DIM),
        ("GitHub Copilot SDK", TEXT, True),
    ], size=17, first=True)

    f = textbox(s, Inches(1.2), Inches(6.35), Inches(11), Inches(0.4))
    write(f, "Alexandre Costa  ·  github.com/magoolation", size=16, color=MUTED, space_after=0, first=True)

    notes(s, "Enquanto a plateia senta: deixe o terminal já aberto com o doctor rodado e verde. "
             "Abra dizendo o que a sessão NÃO é: não é sobre um agente escrever código — todo mundo já viu isso. "
             "É sobre colocar VÁRIOS agentes para trabalhar ao mesmo tempo sem que eles briguem, e sobre quem decide "
             "se o que eles fizeram presta.")


# ===========================================================================
# 2 — Quem sou eu
# ===========================================================================
def slide_speaker():
    s = blank(prs)
    top = heading(s, "Quem está falando", kicker="Apresentação")

    # Sem foto, por escolha do palestrante. O texto então ocupa a largura inteira e
    # cresce: um slide de apresentação com um vazio de 3 polegadas à esquerda parece
    # defeito, e este é o primeiro slide que a plateia lê com atenção.
    left = MARGIN
    width = CONTENT_W

    f = textbox(s, left, top + Inches(0.3), width, Inches(0.9))
    write(f, "Alexandre Costa", size=38, color=WHITE, bold=True, space_after=6, first=True)
    write(f, "Cloud Solution Architect · AI & Apps · Microsoft", size=21, color=ACCENT, space_after=0)

    f = textbox(s, left, top + Inches(1.65), width, Inches(1.5))
    # Texto do palestrante, literal. Não é lugar para edição de terceiro.
    write(f, "Pessoa desenvolvedora com deficiência visual apaixonado por tecnologia e "
             "ativista da diversidade e inclusão. Palestrante internacional, Microsoft MVP "
             "Reconnect e TDC rockStar. Cloud Solution Architect na Microsoft contribui com "
             "a missão de empoderar cada pessoa e organização a conquistar mais.",
          size=21, color=TEXT, space_after=0, line=1.35, first=True)

    # Larguras explícitas: a automática é calibrada para 13pt e corta o texto em 15pt.
    chip(s, left, top + Inches(3.3), "github.com/magoolation", color=ACCENT, width=Inches(2.9), size=15)
    chip(s, left + Inches(3.2), top + Inches(3.3), "linkedin.com/in/magoolation",
         color=VIOLET, width=Inches(3.5), size=15)
    chip(s, left + Inches(7.0), top + Inches(3.3), "@magoolation", color=MUTED, width=Inches(1.9), size=15)

    f = textbox(s, MARGIN, H - Inches(1.55), CONTENT_W, Inches(0.6))
    rich(f, [
        ("Tudo que você vai ver hoje é código aberto: ", MUTED),
        ("github.com/magoolation/tdc-sp-2026-agent-squad", ACCENT, True, MONO),
    ], size=17, first=True)

    notes(s, "Trinta segundos, no máximo um minuto. "
             "Diga uma coisa que te dá credibilidade para falar disso — não o currículo inteiro. "
             "Emende direto na agenda.")


# ===========================================================================
# 3 — Agenda
# ===========================================================================
def slide_agenda():
    s = blank(prs)
    top = heading(s, "Uma hora, quatro demos ao vivo", kicker="Agenda")

    items = [
        ("1", "O problema", "Por que paralelizar agentes é diferente de usar um agente", "8 min"),
        ("2", "A fábrica", "Pipeline, arquitetura, e as duas decisões que sustentam tudo", "10 min"),
        ("3", "Personalização", "As regras da sua empresa como arquivo versionado", "6 min"),
        ("4", "Demos", "Pré-requisitos · reunião → requisitos · agentes em paralelo · PRs", "16 min"),
        ("5", "Os achados", "O que quebrou de verdade, e o que cada quebra ensinou", "10 min"),
        ("6", "Na sua empresa", "Por onde começar, e o que não negociar", "6 min"),
        ("7", "Perguntas", "", "10 min"),
    ]

    y = top + Inches(0.05)

    for number, title, detail, time in items:
        num = box(s, MARGIN, y, Inches(0.52), Inches(0.52), fill=BG_SUNKEN, outline=ACCENT)
        nf = num.text_frame
        nf.vertical_anchor = MSO_ANCHOR.MIDDLE
        nf.margin_left = 0
        nf.margin_right = 0
        write(nf, number, size=17, color=ACCENT, bold=True, align=PP_ALIGN.CENTER, space_after=0, first=True)

        f = textbox(s, MARGIN + Inches(0.8), y + Inches(0.02), Inches(9.3), Inches(0.55))
        rich(f, [(title, TEXT, True), ("    " + detail, MUTED)], size=18, space_after=0, first=True)

        f = textbox(s, W - MARGIN - Inches(1.1), y + Inches(0.05), Inches(1.1), Inches(0.4))
        write(f, time, size=15, color=DIM, align=PP_ALIGN.RIGHT, space_after=0, first=True)

        y = y + Inches(0.72)

    notes(s, "Não leia a agenda item por item — aponte e resuma em vinte segundos. "
             "Destaque só isto: metade do tempo é demo ao vivo, e a parte dos achados é o que "
             "você não encontra em nenhum blog post. Prometa isso; depois entregue.")


# ===========================================================================
# 4 — A promessa vs a realidade
# ===========================================================================
def slide_promise():
    s = blank(prs)
    top = heading(s, "A promessa é velha. O problema, não.", kicker="O problema")

    half = (CONTENT_W - Inches(0.5)) / 2

    left_panel = box(s, MARGIN, top, half, Inches(3.5))
    f = left_panel.text_frame
    f.margin_left = Inches(0.35); f.margin_right = Inches(0.3); f.margin_top = Inches(0.3)
    write(f, "O que já sabemos fazer", size=20, color=WARNING, bold=True, space_after=14, first=True)
    write(f, "Um agente escreve código.", size=24, color=TEXT, bold=True, space_after=12)
    write(f, "Você pede, ele implementa, você revisa. Funciona, é útil, e já é rotina "
             "para muita gente nesta sala.", size=17, color=MUTED, space_after=10)
    write(f, "O gargalo deixou de ser escrever código.", size=17, color=TEXT, space_after=0)

    right_panel = box(s, MARGIN + half + Inches(0.5), top, half, Inches(3.5), outline=ACCENT)
    f = right_panel.text_frame
    f.margin_left = Inches(0.35); f.margin_right = Inches(0.3); f.margin_top = Inches(0.3)
    write(f, "O que ainda não", size=20, color=ACCENT, bold=True, space_after=14, first=True)
    write(f, "Cinco agentes ao mesmo tempo.", size=24, color=WHITE, bold=True, space_after=12)
    write(f, "Quem decide o que cada um faz? Como eles não se atropelam? Quem diz se o "
             "resultado presta — e quem é responsável quando não presta?", size=17, color=MUTED, space_after=10)
    write(f, "É aí que a coisa deixa de ser sobre modelo.", size=17, color=TEXT, space_after=0)

    f = textbox(s, MARGIN, top + Inches(3.85), CONTENT_W, Inches(0.8))
    rich(f, [
        ("O problema não é a inteligência do agente. É a ", MUTED),
        ("engenharia em volta dele", WHITE, True),
        (".", MUTED),
    ], size=23, align=PP_ALIGN.CENTER, first=True)

    notes(s, "Este slide desarma a expectativa. Boa parte da plateia já usa Copilot ou Claude Code. "
             "Se você prometer 'agente que escreve código', perde a sala. "
             "Diga em voz alta: o gargalo não é mais escrever código — é decidir o que escrever, "
             "coordenar quem escreve o quê, e verificar. Essas três coisas são engenharia, não modelo.")


# ===========================================================================
# 5 — Por que paralelizar é difícil
# ===========================================================================
def slide_why_hard():
    s = blank(prs)
    top = heading(s, "Por que dois agentes são muito mais que o dobro de um", kicker="O problema")

    items = [
        ("Conflito. ", "Dois agentes editando OrderService.cs ao mesmo tempo produzem dois PRs que conflitam. "
                       "O paralelismo que você comprou vira retrabalho."),
        ("Dependência. ", "O agente da API precisa do modelo de domínio que o outro ainda está escrevendo. "
                          "Ordem importa, e ninguém a declarou."),
        ("Verificação. ", "O agente diz que terminou. E daí? Quem verifica, e com que autoridade?"),
        ("Escopo. ", "Um agente que 'aproveita e arruma' um arquivo vizinho quebra o PR de outro."),
        ("Responsabilidade. ", "Cinco PRs abertos por robôs. Quem responde pelo que entrou em produção?"),
    ]

    bullets(s, top + Inches(0.1), items, size=19, gap=17)

    panel = box(s, MARGIN, H - Inches(1.85), CONTENT_W, Inches(1.0), fill=BG_SUNKEN, outline=ACCENT)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.vertical_anchor = MSO_ANCHOR.MIDDLE
    rich(f, [
        ("Nenhum desses problemas se resolve com um modelo melhor. ", TEXT, True),
        ("Todos se resolvem com restrição.", ACCENT, True),
    ], size=20, first=True)

    notes(s, "Vá devagar aqui — é a fundação da palestra. "
             "Peça um levantar de mãos: quem já teve dois PRs conflitando por descuido de coordenação humana? "
             "Agora imagine isso automatizado, cinco vezes, sem ninguém olhando. "
             "Fecha com a frase do painel: a solução é restrição, não inteligência.")


# ===========================================================================
# 6 — O que a fábrica faz
# ===========================================================================
def slide_pipeline():
    s = blank(prs)
    top = heading(s, "O pipeline inteiro", kicker="A fábrica")

    stages = [
        ("INTAKE", "solução nova ou\nfeature existente?", ACCENT),
        ("REQUISITOS", "verificáveis +\nas ambiguidades", ACCENT),
        ("PLANO", "work items,\ndependências, ondas", ACCENT),
        ("APROVAÇÃO", "decisão\nhumana", WARNING),
        ("ISSUES", "uma por\nwork item", ACCENT),
        ("AGENTES", "em paralelo,\num worktree cada", VIOLET),
        ("GATE", "format, build,\ntestes, segredos", SUCCESS),
        ("PR POR ITEM", "unidade de\nrevisão", ACCENT),
        ("PR DE ENTREGA", "merge é\nhumano", WARNING),
    ]

    n = len(stages)
    gap = Inches(0.14)
    bw = (CONTENT_W - gap * (n - 1)) / n
    y = top + Inches(0.5)

    for index, (title, detail, color) in enumerate(stages):
        x = MARGIN + (bw + gap) * index
        panel = box(s, x, y, bw, Inches(1.75), fill=BG_RAISED, outline=color)

        f = panel.text_frame
        f.margin_left = Inches(0.08); f.margin_right = Inches(0.08); f.margin_top = Inches(0.16)
        write(f, title, size=12, color=color, bold=True, align=PP_ALIGN.CENTER, space_after=7, first=True)
        write(f, detail, size=11, color=MUTED, align=PP_ALIGN.CENTER, space_after=0, line=1.2)

        if index < n - 1:
            arrow = s.shapes.add_shape(MSO_SHAPE.RIGHT_ARROW, x + bw + Inches(0.01), y + Inches(0.76), gap - Inches(0.02), Inches(0.2))
            arrow.fill.solid(); arrow.fill.fore_color.rgb = BORDER
            arrow.line.fill.background(); arrow.shadow.inherit = False

    gates = box(s, MARGIN, y + Inches(2.2), CONTENT_W, Inches(1.05), fill=BG_SUNKEN, outline=WARNING)
    f = gates.text_frame
    f.margin_left = Inches(0.35); f.vertical_anchor = MSO_ANCHOR.MIDDLE
    rich(f, [("Dois portões humanos: ", WARNING, True),
             ("nada é escrito no GitHub antes da aprovação do plano, e ", TEXT),
             ("nenhum merge acontece sem uma pessoa.", TEXT, True)], size=19, space_after=6, first=True)
    rich(f, [("Entre eles, a fábrica é autônoma — e cada ação irreversível passa por uma verificação determinística.", MUTED)],
         size=16, space_after=0)

    notes(s, "Percorra os nove blocos em ~90 segundos, apontando. Não detalhe ainda — "
             "cada demo vai abrir um deles. "
             "O que precisa ficar: os dois portões amarelos. Diga: 'a fábrica é autônoma no meio, "
             "e amarrada nas pontas — é exatamente aí que ela se torna utilizável numa empresa de verdade'.\n\n"
             "Os dois últimos blocos são níveis diferentes de pull request, e a distinção vai voltar no achado 8: "
             "os PRs por item apontam para o branch de integração e servem para LER uma mudança de cada vez; "
             "o PR de entrega vai do branch de integração para main e é a única coisa que propõe mexer "
             "no que o time entrega. Se alguém perguntar por que dois, a resposta curta é: "
             "'revisar em pedaços, decidir de uma vez'.")


# ===========================================================================
# 7 — Stack
# ===========================================================================
def slide_stack():
    s = blank(prs)
    top = heading(s, "O que está por baixo", kicker="A fábrica")

    rows = [
        ("Orquestração", "Microsoft Agent Framework 1.22", "AIAgent, AgentSession, saída estruturada com schema, middleware, OpenTelemetry", ACCENT),
        ("Modelos", "Microsoft Foundry", "Entra ID, sem nenhuma chave de API. disableLocalAuth no recurso", ACCENT),
        ("Quem escreve código", "GitHub Copilot SDK", "CopilotClient dirigido in-process, exposto como um AIAgent qualquer", VIOLET),
        ("Isolamento", "git worktree", "Um por issue, com COPILOT_HOME próprio e artefatos fora da árvore", SUCCESS),
        ("Tarefas", "GitHub Issues + PRs", "gh CLI — todo comando é copiável pela plateia", ACCENT),
        ("Apresentação", "Console + Blazor + Aspire", "Mesmo fluxo de eventos nos dois; o dashboard do Aspire mostra os spans GenAI", MUTED),
    ]

    y = top
    for label, tech, detail, color in rows:
        f = textbox(s, MARGIN, y, Inches(2.6), Inches(0.4))
        write(f, label, size=15, color=DIM, space_after=0, first=True)

        f = textbox(s, MARGIN + Inches(2.7), y - Inches(0.04), Inches(9.1), Inches(0.75))
        write(f, tech, size=19, color=color, bold=True, space_after=2, first=True)
        write(f, detail, size=14, color=MUTED, space_after=0)

        y = y + Inches(0.88)

    notes(s, "Passe rápido — 60 segundos. A plateia quer ver funcionando, não ouvir lista de tecnologia. "
             "Destaque só dois pontos: 'sem nenhuma chave de API' (Entra ID de ponta a ponta) e "
             "'o Copilot vira um AIAgent como qualquer outro' — é isso que permite compor tudo no mesmo modelo.")


# ===========================================================================
# 8 — Os agentes
# ===========================================================================
def slide_agents():
    s = blank(prs)
    top = heading(s, "Sete papéis, e só um escreve código", kicker="A fábrica")

    rows = [
        ("Transcript Analyst", "gpt-5.5", "Decisões, requisitos e — o que importa — as pendências não resolvidas"),
        ("Intake Analyst", "gpt-5.4-mini", "Greenfield ou brownfield? Lê o repositório antes de opinar"),
        ("Requirements Analyst", "gpt-5.5", "Requisitos verificáveis + no máximo 5 perguntas ao humano"),
        ("Architect", "gpt-5.5", "Work items, dependências, ondas de paralelismo"),
        ("Plan Critic", "gpt-5.5", "Revisa o plano antes de qualquer issue existir"),
        ("Code Reviewer", "gpt-5.3-codex", "Lê o diff citando o identificador da regra violada"),
    ]

    y = top
    for name, model, detail in rows:
        f = textbox(s, MARGIN, y, Inches(3.5), Inches(0.4))
        write(f, name, size=17, color=TEXT, bold=True, space_after=0, first=True)

        chip(s, MARGIN + Inches(3.6), y - Inches(0.03), model, color=ACCENT, size=12)

        f = textbox(s, MARGIN + Inches(6.4), y + Inches(0.02), Inches(5.4), Inches(0.4))
        write(f, detail, size=14, color=MUTED, space_after=0, first=True)

        y = y + Inches(0.62)

    panel = box(s, MARGIN, y + Inches(0.18), CONTENT_W, Inches(0.95), fill=BG_SUNKEN, outline=VIOLET)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.vertical_anchor = MSO_ANCHOR.MIDDLE
    rich(f, [("Implementer", VIOLET, True), ("        ", DIM),
             ("GitHub Copilot", VIOLET, True, MONO), ("        ", DIM),
             ("Escreve o código, em worktree isolado. É o único que pode.", TEXT)], size=18, first=True)

    notes(s, "O ponto deste slide é a separação de papéis. Seis agentes pensam, um escreve. "
             "Se um agente de análise 'quiser' editar um arquivo, ele devolve um finding estruturado — "
             "não edita. Isso é o que torna a coisa auditável: você sabe exatamente quem tocou no quê.")


# ===========================================================================
# 9 — Decisão 1: o gate decide
# ===========================================================================
def slide_decision_gate():
    s = blank(prs)
    top = heading(s, "Decisão 1 — quem tem autoridade para dizer 'está pronto'", kicker="As duas decisões que sustentam tudo")

    half = (CONTENT_W - Inches(0.5)) / 2

    left_panel = box(s, MARGIN, top, half, Inches(2.3), outline=DANGER)
    f = left_panel.text_frame
    f.margin_left = Inches(0.32); f.margin_top = Inches(0.26); f.margin_right = Inches(0.25)
    write(f, "O que não pode decidir", size=16, color=DANGER, bold=True, space_after=12, first=True)
    write(f, "\"Implementei e testei, está tudo funcionando.\"", size=19, color=TEXT, space_after=10)
    write(f, "É a opinião do agente sobre o próprio trabalho. Não é evidência.", size=15, color=MUTED, space_after=0)

    right_panel = box(s, MARGIN + half + Inches(0.5), top, half, Inches(2.3), outline=SUCCESS)
    f = right_panel.text_frame
    f.margin_left = Inches(0.32); f.margin_top = Inches(0.26); f.margin_right = Inches(0.25)
    write(f, "O que decide", size=16, color=SUCCESS, bold=True, space_after=12, first=True)
    write(f, "dotnet build -warnaserror  →  exit 0", size=17, color=TEXT, font=MONO, space_after=6)
    write(f, "dotnet test                →  exit 0", size=17, color=TEXT, font=MONO, space_after=10)
    write(f, "Um programa, com a mesma resposta toda vez.", size=15, color=MUTED, space_after=0)

    f = textbox(s, MARGIN, top + Inches(2.65), CONTENT_W, Inches(1.5))
    write(f, "O gate roda sete etapas, e todas têm que passar:", size=17, color=MUTED, space_after=14, first=True)

    steps = ["restore", "format", "build -warnaserror", "testes", "vulnerabilidades", "escopo", "segredos"]
    x = MARGIN
    for step in steps:
        shape = chip(s, x, top + Inches(3.15), step, color=SUCCESS, size=13)
        x = x + shape.width + Inches(0.16)

    panel = box(s, MARGIN, H - Inches(1.75), CONTENT_W, Inches(0.95), fill=BG_SUNKEN, outline=BORDER)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.vertical_anchor = MSO_ANCHOR.MIDDLE
    rich(f, [("Nenhum pull request é aberto sem isso. ", TEXT, True),
             ("Num sistema autônomo, a única coisa em que você pode confiar é no que um programa verificou.", MUTED)],
         size=18, first=True)

    notes(s, "Este é o slide mais importante da palestra depois do próximo. "
             "Frase para repetir: 'o modelo produz, o programa verifica'. "
             "Se sobrar só uma ideia na cabeça da plateia, que seja esta.")


# ===========================================================================
# 10 — Decisão 2: conflito de arquivos
# ===========================================================================
def slide_decision_conflict():
    s = blank(prs)
    top = heading(s, "Decisão 2 — a regra que faz o paralelismo funcionar", kicker="As duas decisões que sustentam tudo")

    panel = box(s, MARGIN, top, CONTENT_W, Inches(1.05), fill=BG_SUNKEN, outline=ACCENT)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.vertical_anchor = MSO_ANCHOR.MIDDLE
    write(f, "Dois work items da mesma onda nunca declaram o mesmo arquivo.",
          size=25, color=WHITE, bold=True, align=PP_ALIGN.CENTER, space_after=0, first=True)

    f = textbox(s, MARGIN, top + Inches(1.35), CONTENT_W, Inches(0.5))
    write(f, "Quando dois itens querem o mesmo arquivo, existem exatamente três saídas honestas:",
          size=17, color=MUTED, space_after=0, first=True)

    options = [
        ("1", "Sequenciar", "mover um para uma onda posterior, com dependência declarada"),
        ("2", "Extrair uma costura", "um item anterior cria a interface que permite trabalhar em paralelo"),
        ("3", "Fundir", "se são pequenos e inseparáveis, são um item só"),
    ]

    third = (CONTENT_W - Inches(0.6)) / 3
    for index, (number, title, detail) in enumerate(options):
        x = MARGIN + (third + Inches(0.3)) * index
        card = box(s, x, top + Inches(1.95), third, Inches(1.55))
        f = card.text_frame
        f.margin_left = Inches(0.28); f.margin_right = Inches(0.22); f.margin_top = Inches(0.24)
        write(f, number, size=15, color=ACCENT, bold=True, space_after=6, first=True)
        write(f, title, size=20, color=TEXT, bold=True, space_after=8)
        write(f, detail, size=14, color=MUTED, space_after=0, line=1.2)

    panel = box(s, MARGIN, H - Inches(1.9), CONTENT_W, Inches(1.1), fill=BG_RAISED, outline=SUCCESS)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.vertical_anchor = MSO_ANCHOR.MIDDLE
    rich(f, [("E aqui está o pulo do gato: ", MUTED),
             ("essa regra é verificada por código, não confiada ao modelo.", WHITE, True)],
         size=19, space_after=5, first=True)
    rich(f, [("PlanValidator", SUCCESS, True, MONO),
             ("  — 11 verificações determinísticas no plano que o modelo produziu, antes de qualquer issue existir.", MUTED)],
         size=15, space_after=0)

    notes(s, "Segunda frase para repetir. Explique o custo concreto: dois agentes, dois worktrees, "
             "o mesmo arquivo — dois PRs que conflitam, e você trocou paralelismo por resolução de conflito. "
             "O detalhe que impressiona: é um modelo que propõe o plano, mas é um PROGRAMA que decide se "
             "ele pode rodar. Se o validador reprova, o plano volta para o arquiteto com o código do erro.")


# ===========================================================================
# Demo divider
# ===========================================================================
# The exact, copy-pasteable command for each demo. It lives in the speaker notes
# rather than on the slide: the slide shows the short form the audience should
# read, the notes show what actually has to be typed.
DEMO_NOTES = {
    1: 'COMANDO EXATO — terminal na raiz do repositório da fábrica (E:\\source\\repos\\TDCSP2026):\n\ndotnet run --project src/AgentSquad.Cli -- doctor --probe-models\n\nLeva ~40 s, quase tudo esperando o gpt-5.5 — que é o ponto: é chamada real a cada modelo, não checagem de configuração.\n\nANTES DE SUBIR AO PALCO: rode uma vez "dotnet build AgentSquad.slnx -c Release" e depois acrescente "-c Release --no-build" a TODOS os comandos das demos. Sem isso você espera compilação na frente da plateia.\n\nSe quiser provar também o Copilot (24 s, isolado de propósito):\ndotnet run --project src/AgentSquad.Cli -- doctor --probe-copilot\n\nO que apontar: cada falha traz o comando que corrige, não "algo deu errado". Repare nos 37 s do gpt-5.5 — modelo de raciocínio tem custo de latência, e isso volta no achado 2.',
    2: 'COMANDO EXATO:\n\ndotnet run --project src/AgentSquad.Cli -- run `\n  --transcript samples/meeting-transcripts/kickoff-catalogo.md `\n  --repo squad-demo-catalogo --plan-only\n\n--plan-only para depois do plano e NÃO escreve nada no GitHub. Pode repetir quantas vezes quiser, inclusive se algo der errado no palco. É a demo mais segura da apresentação.\n\nEnquanto o agente lê a transcrição (~60 s), conte como o arquivo foi escrito: um requisito que ninguém quantificou, uma divergência que ficou sem resolução, um "obviamente" que não é óbvio, um escopo que cresceu no meio da conversa.\n\nO teste de fogo: o agente lista o p95 como PENDÊNCIA ou registra como requisito? O gabarito está no fim do próprio arquivo da transcrição.',
    3: 'COMANDO EXATO — esta é a execução de verdade: escreve issues, branches e pull requests no GitHub.\n\ndotnet run --project src/AgentSquad.Cli -- run `\n  --request "Preciso de uma API REST de catálogo de produtos em .NET: busca paginada por nome, filtro por categoria e faixa de preço, com testes de integração." `\n  --repo squad-demo-catalogo --parallel 3\n\nSEM --plan-only (você quer que escreva) e SEM --unattended (você quer mostrar a aprovação humana do plano).\n\nCom a interface web e o dashboard do Aspire, em outro terminal:\ndotnet run --project src/AgentSquad.AppHost\n\nTempos: intake ~40 s · requisitos ~90 s · planejamento 4 a 6 min · agentes 3 a 20 min por onda. Você NÃO vai esperar em silêncio — o roteiro tem o que falar em cada espera.\n\nSe o gate reprovar, COMEMORE e pare para mostrar o diagnóstico voltando para o agente.',
    4: 'COMANDO EXATO:\n\ngh pr list --repo magoolation/squad-demo-catalogo\n\nPara abrir um no navegador direto do terminal:\ngh pr view <numero> --repo magoolation/squad-demo-catalogo --web\n\nOrdem: abra primeiro um PR de ITEM (tabela do gate, apontamento citando TST-004 com arquivo:linha, bloco de procedência). Depois o PR de ENTREGA, o que vai de agent/run-... para main: o que entrou, o que NÃO entrou, e a última linha, "O merge é seu."\n\nExplique os dois níveis ANTES que alguém pergunte: os PRs por item apontam para o branch de integração e servem para ler uma mudança de cada vez; o de entrega é a única coisa que propõe mexer no main. Alguns PRs de item aparecem como MERGED — o GitHub faz isso sozinho quando os commits viram alcançáveis pela base. Emende no achado 8.\n\nEm uma frase: "revisar em pedaços, decidir de uma vez".',
}


def slide_demo(number, title, subtitle, commands, talking, duration):
    s = blank(prs)
    plain.append(s)

    box(s, 0, 0, Inches(0.16), H, fill=VIOLET, outline=None, radius=False)

    f = textbox(s, Inches(1.15), Inches(0.75), Inches(11), Inches(0.45))
    rich(f, [(f"DEMO {number}", VIOLET, True), (f"        {duration}", DIM)], size=16, space_after=0, first=True)

    f = textbox(s, Inches(1.15), Inches(1.25), Inches(11.2), Inches(1.2))
    write(f, title, size=42, color=WHITE, bold=True, space_after=6, first=True)
    write(f, subtitle, size=20, color=MUTED, space_after=0)

    code_panel(s, Inches(1.15), Inches(3.0), Inches(11.0), commands, size=15)

    f = textbox(s, Inches(1.15), Inches(5.55), Inches(11.0), Inches(1.3))
    write(f, "O que apontar:", size=15, color=VIOLET, bold=True, space_after=9, first=True)
    for line in talking:
        rich(f, [("▸  ", VIOLET, True), (line, TEXT)], size=16, space_after=7)

    if number in DEMO_NOTES:
        notes(s, DEMO_NOTES[number])

    return s


# ===========================================================================
# 12 — Personalização
# ===========================================================================
def slide_customization():
    s = blank(prs)
    top = heading(s, "As regras da empresa não vivem num prompt escondido", kicker="Personalização")

    tree = [
        ("AGENTS.md", ACCENT, "contrato de trabalho — lido pelo Copilot CLI e injetado no prompt"),
        ("docs/engineering-rules.md", ACCENT, "as regras, com identificador estável: ENG-042, SEC-004, TST-004"),
        (".github/copilot-instructions.md", MUTED, "instruções do repositório"),
        (".github/instructions/*.md", MUTED, "instruções por caminho de arquivo, aplicadas automaticamente"),
        (".github/agents/*.md", VIOLET, "agentes customizados: implementer, planner, reviewer, test-author"),
        (".github/skills/<nome>/SKILL.md", VIOLET, "habilidades reutilizáveis, carregadas sob demanda"),
    ]

    y = top
    for path, color, detail in tree:
        f = textbox(s, MARGIN, y, Inches(5.0), Inches(0.4))
        write(f, path, size=16, color=color, bold=True, font=MONO, space_after=0, first=True)

        f = textbox(s, MARGIN + Inches(5.2), y + Inches(0.02), Inches(6.6), Inches(0.4))
        write(f, detail, size=14, color=MUTED, space_after=0, first=True)

        y = y + Inches(0.6)

    panel = box(s, MARGIN, y + Inches(0.2), CONTENT_W, Inches(1.3), fill=BG_SUNKEN, outline=SUCCESS)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.margin_top = Inches(0.24)
    write(f, "Tudo isso é arquivo versionado, revisável em pull request.", size=19, color=WHITE, bold=True, space_after=8, first=True)
    rich(f, [("Para adaptar à sua empresa você edita dois arquivos. ", MUTED),
             ("Nenhuma linha de C#.", TEXT, True)], size=16, space_after=0)

    notes(s, "Aqui é onde a plateia corporativa acorda. A pergunta que todo tech lead faz é "
             "'como eu faço ele seguir o NOSSO padrão?'. A resposta: não é fine-tuning, não é prompt secreto — "
             "é arquivo no repositório, que passa por code review como qualquer outro. "
             "Mostre o comando 'copilot skill list' ao vivo se sobrar tempo.")


# ===========================================================================
# 13 — Regras executáveis
# ===========================================================================
def slide_rules_executable():
    s = blank(prs)
    top = heading(s, "Uma regra que ninguém aplica é um comentário", kicker="Personalização")

    rows = [
        (".editorconfig", "dotnet format --verify-no-changes reprova o PR"),
        ("Directory.Build.props", "TreatWarningsAsErrors + analisadores como erro"),
        (".editorconfig (CA5xxx)", "toda regra de segurança do .NET escalada para error"),
        ("Directory.Packages.props", "Central Package Management: nenhuma versão solta"),
        ("engineering-rules.md", "citada por identificador em cada apontamento bloqueante"),
        ("AGENTS.md §3", "regras invioláveis — o agente é instruído a recusar"),
        ("CopilotOptions", "a política de permissões impede tecnicamente, não pede"),
    ]

    y = top
    for where, how in rows:
        f = textbox(s, MARGIN, y, Inches(4.2), Inches(0.4))
        write(f, where, size=15, color=ACCENT, font=MONO, space_after=0, first=True)

        arrow = s.shapes.add_shape(MSO_SHAPE.RIGHT_ARROW, MARGIN + Inches(4.35), y + Inches(0.09), Inches(0.3), Inches(0.12))
        arrow.fill.solid(); arrow.fill.fore_color.rgb = BORDER
        arrow.line.fill.background(); arrow.shadow.inherit = False

        f = textbox(s, MARGIN + Inches(4.85), y, Inches(6.9), Inches(0.4))
        write(f, how, size=15, color=TEXT, space_after=0, first=True)

        y = y + Inches(0.46)

    code_panel(s, MARGIN, y + Inches(0.18), CONTENT_W, [
        ('"rule": "SEC-004",   "severity": "blocking",', DANGER),
        ('"file": "src/Tools/Git/GitCli.cs",   "line": 88,', TEXT),
        ('"finding": "ProcessStartInfo.Arguments montado por concatenação...",', MUTED),
        ('"fix": "Trocar para ArgumentList.Add(...) por argumento."', SUCCESS),
    ], size=13, height=Inches(1.55))

    notes(s, "O painel embaixo é a prova: o revisor automatizado não diz 'achei feio'. "
             "Ele cita SEC-004, arquivo, linha, e a correção concreta. "
             "Diga: um revisor que não sabe nomear a regra que está aplicando está expressando gosto — "
             "e gosto não pode bloquear um pipeline.")


# ===========================================================================
# 16 — O que um resumo comum erra
# ===========================================================================
def slide_transcript_insight():
    s = blank(prs)
    top = heading(s, "O valor não é resumir a reunião", kicker="Da reunião ao requisito")

    code_panel(s, MARGIN, top, CONTENT_W, [
        ("[03:10] Juliana:  Dá pra trabalhar com p95 abaixo de 300 ms?", TEXT),
        ("[03:20] Renata:   Se for isso eu assino embaixo.", TEXT),
        ("[03:24] Caio:     Anota como meta, não como SLA.", WARNING),
    ], size=17)

    half = (CONTENT_W - Inches(0.5)) / 2
    y = top + Inches(1.85)

    left_panel = box(s, MARGIN, y, half, Inches(2.25), outline=DANGER)
    f = left_panel.text_frame
    f.margin_left = Inches(0.3); f.margin_top = Inches(0.24); f.margin_right = Inches(0.24)
    write(f, "O que um resumo comum produz", size=15, color=DANGER, bold=True, space_after=12, first=True)
    write(f, "RNF-03: p95 < 300 ms", size=19, color=TEXT, bold=True, font=MONO, space_after=10)
    write(f, "Requisito acordado. Vai para o plano, vira critério de aceite, alguém implementa.", size=15, color=MUTED, space_after=8)
    write(f, "E está errado.", size=16, color=DANGER, bold=True, space_after=0)

    right_panel = box(s, MARGIN + half + Inches(0.5), y, half, Inches(2.25), outline=SUCCESS)
    f = right_panel.text_frame
    f.margin_left = Inches(0.3); f.margin_top = Inches(0.24); f.margin_right = Inches(0.24)
    write(f, "O que o analista precisa produzir", size=15, color=SUCCESS, bold=True, space_after=12, first=True)
    write(f, "PENDÊNCIA não resolvida", size=19, color=TEXT, bold=True, font=MONO, space_after=10)
    write(f, "Três pessoas, dois entendimentos, zero reconciliação. Todo mundo saiu da sala achando que estava combinado.", size=15, color=MUTED, space_after=0)

    panel = box(s, MARGIN, H - Inches(1.35), CONTENT_W, Inches(0.8), fill=BG_SUNKEN, outline=ACCENT)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.vertical_anchor = MSO_ANCHOR.MIDDLE
    write(f, "Separar o que foi decidido do que só pareceu decidido.", size=21, color=WHITE, bold=True, align=PP_ALIGN.CENTER, space_after=0, first=True)

    notes(s, "Este é o slide que as pessoas fotografam. "
             "A transcrição de exemplo foi escrita de propósito com as patologias de uma reunião real: "
             "um requisito que ninguém quantificou, uma divergência sem resolução, um 'obviamente' que não é óbvio, "
             "e uma decisão tomada por quem chegou atrasado. "
             "Se o agente acertar ao vivo, pare e deixe a plateia ler. Se errar, também pare — "
             "e explique que é exatamente por isso que existe aprovação humana.")


# ===========================================================================
# 17 — HITL
# ===========================================================================
def slide_hitl():
    s = blank(prs)
    top = heading(s, "Perguntar bem é mais difícil que responder", kicker="Human-in-the-loop")

    half = (CONTENT_W - Inches(0.5)) / 2

    left_panel = box(s, MARGIN, top, half, Inches(2.6), outline=DANGER)
    f = left_panel.text_frame
    f.margin_left = Inches(0.3); f.margin_top = Inches(0.24); f.margin_right = Inches(0.24)
    write(f, "Pergunta inútil", size=15, color=DANGER, bold=True, space_after=14, first=True)
    write(f, "\"Qual estratégia de cache você prefere?\"", size=18, color=TEXT, space_after=12)
    write(f, "Aberta. Quem responde precisa já saber a resposta — e se soubesse, não precisaria do agente.", size=15, color=MUTED, space_after=0)

    right_panel = box(s, MARGIN + half + Inches(0.5), top, half, Inches(2.6), outline=SUCCESS)
    f = right_panel.text_frame
    f.margin_left = Inches(0.3); f.margin_top = Inches(0.24); f.margin_right = Inches(0.24)
    write(f, "Pergunta útil", size=15, color=SUCCESS, bold=True, space_after=14, first=True)
    write(f, "\"Os pedidos precisam sobreviver a uma queda antes da confirmação?\"", size=17, color=TEXT, space_after=10)
    rich(f, [("Sim ", SUCCESS, True), ("→ outbox + fila, +3 issues, +1 dia", MUTED)], size=14, space_after=4)
    rich(f, [("Não ", SUCCESS, True), ("→ chamada síncrona, plano menor", MUTED)], size=14, space_after=0)

    rules_list = [
        ("No máximo 5 por rodada. ", "A paciência de quem responde é finita — numa palestra, mais ainda."),
        ("Cada opção mostra o impacto no plano. ", "A escolha é entre consequências, não entre rótulos."),
        ("Toda pergunta tem um padrão declarado. ", "O sistema tem que poder seguir sozinho."),
        ("Só pergunta o que muda o plano. ", "Se dá para decidir sozinho, decide — e registra como suposição contestável."),
    ]
    bullets(s, top + Inches(2.95), rules_list, size=16, gap=11)

    notes(s, "Uma pergunta ruim é pior que nenhuma pergunta: consome atenção e não melhora a decisão. "
             "O critério que uso: a pergunta só existe se (a) muda o PLANO, (b) não dá para responder lendo o "
             "repositório, e (c) escolher errado é caro de desfazer. Falhou em qualquer um dos três? "
             "O agente decide e registra como suposição, para o humano contestar na aprovação.")


# ===========================================================================
# 19 — Validador vs crítico
# ===========================================================================
def slide_validator_critic():
    s = blank(prs)
    top = heading(s, "Dois revisores de plano, com autoridades diferentes", kicker="Plano e paralelismo")

    half = (CONTENT_W - Inches(0.5)) / 2

    left_panel = box(s, MARGIN, top, half, Inches(3.3), outline=SUCCESS)
    f = left_panel.text_frame
    f.margin_left = Inches(0.32); f.margin_top = Inches(0.26); f.margin_right = Inches(0.26)
    write(f, "VALIDADOR DETERMINÍSTICO", size=14, color=SUCCESS, bold=True, space_after=6, first=True)
    write(f, "É o portão", size=24, color=WHITE, bold=True, space_after=12)
    write(f, "11 verificações com resposta certa:", size=15, color=MUTED, space_after=8)
    for item in ["conflito de arquivo na mesma onda", "dependência em onda posterior", "ciclo no grafo",
                 "item grande demais", "critério de aceite ausente", "requisito sem cobertura"]:
        rich(f, [("·  ", SUCCESS), (item, TEXT)], size=14, space_after=4)

    right_panel = box(s, MARGIN + half + Inches(0.5), top, half, Inches(3.3), outline=WARNING)
    f = right_panel.text_frame
    f.margin_left = Inches(0.32); f.margin_top = Inches(0.26); f.margin_right = Inches(0.26)
    write(f, "AGENTE CRÍTICO", size=14, color=WARNING, bold=True, space_after=6, first=True)
    write(f, "É conselho", size=24, color=WHITE, bold=True, space_after=12)
    write(f, "Procura o que um programa não vê:", size=15, color=MUTED, space_after=8)
    for item in ["acoplamento escondido entre itens", "critério que nenhum teste resolve",
                 "trabalho transversal esquecido", "escopo que cresceu sozinho"]:
        rich(f, [("·  ", WARNING), (item, TEXT)], size=14, space_after=4)

    panel = box(s, MARGIN, H - Inches(1.85), CONTENT_W, Inches(1.05), fill=BG_SUNKEN, outline=BORDER)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.vertical_anchor = MSO_ANCHOR.MIDDLE
    rich(f, [("Em três execuções o crítico nunca aprovou. ", WARNING, True),
             ("Um modelo revisor sempre acha mais uma coisa a dizer.", TEXT)], size=18, space_after=5, first=True)
    rich(f, [("A correção foi hierárquica, não de prompt: validador limpo + uma rodada de crítica bastam, "
              "e o resto vai para o humano. 8,0 min → 5,9 min.", MUTED)], size=15, space_after=0)

    notes(s, "Adiante um dos achados aqui — ele encaixa perfeitamente. "
             "Eu pedi ao crítico, no prompt, para aprovar quando o plano estivesse bom. Ele nunca aprovou. "
             "A lição não é 'melhore o prompt': é que você não pode dar poder de veto a algo que não sabe "
             "parar de opinar. Validador é portão, crítico é conselho.")


# ===========================================================================
# 21 — O gate
# ===========================================================================
def slide_gate():
    s = blank(prs)
    top = heading(s, "O que acontece quando o gate reprova", kicker="Implementação")

    steps = [
        ("1", "O agente diz que terminou", MUTED),
        ("2", "O gate roda e discorda", WARNING),
        ("3", "O diagnóstico do compilador volta verbatim para o agente", ACCENT),
        ("4", "Ele corrige e roda de novo — até 2 tentativas", ACCENT),
        ("5", "Esgotou? PR aberto como draft, marcado needs-human, com o diagnóstico no corpo", DANGER),
    ]

    y = top
    for number, text, color in steps:
        num = box(s, MARGIN, y, Inches(0.46), Inches(0.46), fill=BG_SUNKEN, outline=color)
        nf = num.text_frame
        nf.vertical_anchor = MSO_ANCHOR.MIDDLE; nf.margin_left = 0; nf.margin_right = 0
        write(nf, number, size=15, color=color, bold=True, align=PP_ALIGN.CENTER, space_after=0, first=True)

        f = textbox(s, MARGIN + Inches(0.72), y + Inches(0.06), Inches(10.8), Inches(0.5))
        write(f, text, size=18, color=TEXT, space_after=0, first=True)

        y = y + Inches(0.68)

    panel = box(s, MARGIN, y + Inches(0.3), CONTENT_W, Inches(1.5), fill=BG_RAISED, outline=SUCCESS)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.margin_top = Inches(0.26); f.margin_right = Inches(0.3)
    write(f, "Se isso acontecer ao vivo hoje, é para comemorar.", size=21, color=WHITE, bold=True, space_after=10, first=True)
    write(f, "Um sistema autônomo que nunca é contrariado não está sendo verificado. "
             "A parte interessante não é o agente acertar de primeira — é o que acontece quando ele erra.",
          size=16, color=MUTED, space_after=0)

    notes(s, "PREPARE-SE para isso acontecer. Na minha execução de validação, o gate reprovou duas vezes "
             "antes de aprovar. Se acontecer, use o roteiro: 'repararam? o agente disse que terminou, "
             "o compilador discordou, e o diagnóstico volta para ele'. "
             "Isso vende a ideia melhor do que qualquer slide.")


# ===========================================================================
# 22 — Segurança
# ===========================================================================
def slide_security():
    s = blank(prs)
    top = heading(s, "Um agente autônomo é uma superfície de ataque", kicker="Segurança")

    rows = [
        ("Agência excessiva", "LLM08", "git push, gh pr e az são NEGADOS pela política. Negação vence qualquer aprovação automática."),
        ("Prompt injection", "LLM01", "Transcrição, README e issue entram delimitados como dado, nunca como instrução."),
        ("Exfiltração", "LLM06", "Fetch restrito a allow-list. .env, .ssh e .pem negados na leitura."),
        ("Escape de sandbox", "—", "Caminho normalizado e comparado com separador final. C:\\wt\\i42-evil não satisfaz C:\\wt\\i42."),
        ("Injeção de comando", "—", "ArgumentList sempre. Há teste que passa `a && whoami` como argumento e confere o round-trip."),
        ("Segredos", "SEC-001", "Zero chave no repositório. Foundry com disableLocalAuth. Varredura no diff antes do PR."),
    ]

    y = top
    for risk, code, control in rows:
        f = textbox(s, MARGIN, y, Inches(2.5), Inches(0.4))
        write(f, risk, size=16, color=TEXT, bold=True, space_after=0, first=True)

        if code != "—":
            chip(s, MARGIN + Inches(2.6), y - Inches(0.03), code, color=DANGER, size=11)

        f = textbox(s, MARGIN + Inches(3.9), y + Inches(0.02), Inches(7.9), Inches(0.5))
        write(f, control, size=14, color=MUTED, space_after=0, first=True)

        y = y + Inches(0.66)

    panel = box(s, MARGIN, y + Inches(0.15), CONTENT_W, Inches(0.95), fill=BG_SUNKEN, outline=ACCENT)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.vertical_anchor = MSO_ANCHOR.MIDDLE
    write(f, "Dentro do worktree o agente é autônomo. Fora dele, é impotente.",
          size=20, color=WHITE, bold=True, align=PP_ALIGN.CENTER, space_after=0, first=True)

    notes(s, "Slide obrigatório para plateia corporativa — é a primeira pergunta do time de segurança. "
             "O ponto central: não é que a gente PEÇA ao agente para não dar push. "
             "A política de permissões do Copilot SDK IMPEDE, e negação vence aprovação automática. "
             "Isso é diferente de instrução em prompt, e a diferença importa numa auditoria.")


# ===========================================================================
# 25 — Achados: abertura
# ===========================================================================
def slide_findings_intro():
    s = blank(prs)
    plain.append(s)

    box(s, 0, 0, Inches(0.16), H, fill=WARNING, outline=None, radius=False)

    f = textbox(s, Inches(1.15), Inches(1.9), Inches(11), Inches(0.45))
    write(f, "A PARTE QUE VOCÊ NÃO ENCONTRA EM BLOG POST", size=15, color=WARNING, bold=True, space_after=0, first=True)

    f = textbox(s, Inches(1.15), Inches(2.4), Inches(11.2), Inches(1.6))
    write(f, "Oito coisas que quebraram", size=54, color=WHITE, bold=True, space_after=8, first=True)
    write(f, "e o que cada uma ensinou", size=30, color=MUTED, space_after=0)

    f = textbox(s, Inches(1.15), Inches(4.6), Inches(11), Inches(1.2))
    write(f, "Nenhuma delas aparece em teste unitário. Todas apareceram rodando o sistema de verdade, "
             "contra a nuvem de verdade, num repositório de verdade.",
          size=19, color=MUTED, space_after=0, first=True)

    notes(s, "Transição importante. Diga: 'a partir daqui eu paro de te vender a solução e começo a te contar "
             "o que deu errado'. Plateia técnica confia muito mais em quem mostra as cicatrizes. "
             "Estes oito achados custaram horas cada um. E o oitavo é consequência direta da correção do sétimo.")


def finding(number, title, symptom, lesson, code_lines=None, symptom_color=DANGER):
    s = blank(prs)
    top = heading(s, title, kicker=f"Achado {number} de 8")

    panel = box(s, MARGIN, top, CONTENT_W, Inches(1.15), fill=BG_RAISED, outline=symptom_color)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.margin_top = Inches(0.2); f.margin_right = Inches(0.3)
    write(f, "O SINTOMA", size=12, color=symptom_color, bold=True, space_after=7, first=True)
    write(f, symptom, size=17, color=TEXT, space_after=0, line=1.2)

    y = top + Inches(1.45)

    if code_lines:
        code_panel(s, MARGIN, y, CONTENT_W, code_lines, size=14)
        y = y + Inches(0.34) * len(code_lines) + Inches(0.75)

    panel = box(s, MARGIN, y, CONTENT_W, Inches(1.5), fill=BG_SUNKEN, outline=SUCCESS)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.margin_top = Inches(0.22); f.margin_right = Inches(0.3)
    write(f, "A LIÇÃO", size=12, color=SUCCESS, bold=True, space_after=8, first=True)
    write(f, lesson, size=18, color=TEXT, space_after=0, line=1.25)

    return s


# ===========================================================================
# 33 — O padrão
# ===========================================================================
def slide_pattern():
    s = blank(prs)
    top = heading(s, "O padrão por trás dos oito", kicker="Os achados")

    items = [
        ("Nenhum era sobre o modelo. ", "Todos eram integração, versão, permissão ou hierarquia de autoridade."),
        ("Nenhum aparecia em teste unitário. ", "Todos apareceram rodando contra a nuvem de verdade."),
        ("Quatro só apareceram depois de 10 minutos de execução. ", "Por isso cada integração arriscada virou um diagnóstico que roda em segundos."),
        ("O mais caro foi o que culpava a parte errada. ", "Um gate com bug faz um agente correto parecer incompetente — e queima as tentativas de reparo."),
        ("Um deles foi criado pela correção de outro. ", "Consertar o fluxo das ondas passou a fechar PRs que ninguém revisou. Toda correção merece a mesma desconfiança."),
    ]
    bullets(s, top + Inches(0.1), items, size=19, gap=18)

    panel = box(s, MARGIN, H - Inches(2.1), CONTENT_W, Inches(1.25), fill=BG_RAISED, outline=ACCENT)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.margin_top = Inches(0.24); f.margin_right = Inches(0.3)
    write(f, "Se você for construir algo assim, orce o dobro do tempo para integração — "
             "e nada para 'fazer o modelo entender'.", size=20, color=WHITE, bold=True, space_after=8, first=True)
    write(f, "O modelo é a parte que já funciona.", size=17, color=MUTED, space_after=0)

    notes(s, "Este slide é a tese da palestra em quatro linhas. "
             "Se alguém for embora depois disso, levou o essencial: "
             "construir uma fábrica de agentes é 20% prompt e 80% engenharia de integração e verificação.")


# ===========================================================================
# 34-35 — Como fazer na sua empresa
# ===========================================================================
def slide_how_order():
    s = blank(prs)
    top = heading(s, "Por onde começar na segunda-feira", kicker="Na sua empresa")

    steps = [
        ("1", "Escreva as regras antes do agente", "docs/engineering-rules.md com identificador estável. Sem isso, você não tem como dizer se o agente acertou.", SUCCESS),
        ("2", "Automatize o gate para humanos primeiro", "Se o seu CI não reprova um PR humano mal formatado, ele não vai reprovar o de um agente.", SUCCESS),
        ("3", "Um agente, uma issue, um worktree", "Não comece com cinco. Faça um funcionar de ponta a ponta, incluindo o PR.", ACCENT),
        ("4", "Só então paralelize", "E no minuto em que paralelizar, implemente a regra do conflito de arquivo.", ACCENT),
        ("5", "Meça antes de otimizar", "Quanto custa uma execução? Quantos reparos por item? Onde o tempo vai?", WARNING),
    ]

    y = top
    for number, title, detail, color in steps:
        num = box(s, MARGIN, y, Inches(0.5), Inches(0.5), fill=BG_SUNKEN, outline=color)
        nf = num.text_frame
        nf.vertical_anchor = MSO_ANCHOR.MIDDLE; nf.margin_left = 0; nf.margin_right = 0
        write(nf, number, size=16, color=color, bold=True, align=PP_ALIGN.CENTER, space_after=0, first=True)

        f = textbox(s, MARGIN + Inches(0.78), y - Inches(0.02), Inches(11.0), Inches(0.8))
        write(f, title, size=19, color=TEXT, bold=True, space_after=3, first=True)
        write(f, detail, size=15, color=MUTED, space_after=0)

        y = y + Inches(1.0)

    notes(s, "Slide prático — a plateia está esperando por ele. "
             "Enfatize o passo 2: se o CI de vocês hoje não reprova um PR humano mal formatado, "
             "automatizar agente é prematuro. O gate é pré-requisito, não consequência.")


def slide_how_nonnegotiable():
    s = blank(prs)
    top = heading(s, "O que não negociar", kicker="Na sua empresa")

    items = [
        ("Verificação determinística. ", "O modelo produz, o programa verifica. Sempre."),
        ("Um PR que proponha mexer no main. ", "E que nenhum agente tenha permissão de aprovar."),
        ("Sandbox técnico, não instrução. ", "Impedir é diferente de pedir. Numa auditoria, muito diferente."),
        ("Teto de gasto e de iteração. ", "Tentativas, timeout por agente, timeout por execução, limite de concorrência."),
        ("Auditabilidade. ", "Prompt, modelo, tokens, ferramentas chamadas, permissões negadas e diff — tudo gravado."),
        ("Transparência. ", "Todo PR de agente é rotulado e declara qual modelo o produziu."),
    ]
    bullets(s, top + Inches(0.1), items, size=18, gap=16)

    panel = box(s, MARGIN, H - Inches(1.6), CONTENT_W, Inches(0.85), fill=BG_SUNKEN, outline=WARNING)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.vertical_anchor = MSO_ANCHOR.MIDDLE
    write(f, "Essas seis coisas são o que separa uma demo de um sistema que você pode colocar numa empresa.",
          size=18, color=TEXT, align=PP_ALIGN.CENTER, space_after=0, first=True)

    notes(s, "Leia devagar. Cada item aqui é uma conversa que você vai ter com segurança, com compliance, "
             "ou com o seu diretor. Ter a resposta pronta é o que faz o projeto ser aprovado.")


# ===========================================================================
# 36 — O que não resolvi
# ===========================================================================
def slide_limits():
    s = blank(prs)
    top = heading(s, "O que eu não resolvi", kicker="Honestidade")

    items = [
        ("Custo real por entrega. ", "Medi tempo, não dinheiro. Uma execução de 40 min consome tokens de 6 agentes."),
        ("Brownfield grande de verdade. ", "Validei greenfield e mudanças pequenas. Repositório legado de 500 mil linhas é outro problema."),
        ("Retomada de execução. ", "Uma execução interrompida recomeça do zero. O journal preserva o histórico, mas não o estado."),
        ("Qualidade sustentada. ", "Cinco PRs aprovados no gate não são cinco PRs que eu mandaria para produção sem ler."),
        ("O crítico ainda é barulhento. ", "Melhorou com a mudança de hierarquia, mas ainda aponta mais do que deveria."),
    ]
    bullets(s, top + Inches(0.1), items, size=18, gap=17)

    panel = box(s, MARGIN, H - Inches(1.7), CONTENT_W, Inches(0.95), fill=BG_RAISED, outline=BORDER)
    f = panel.text_frame
    f.margin_left = Inches(0.35); f.vertical_anchor = MSO_ANCHOR.MIDDLE
    write(f, "Se alguém te vender isso como resolvido, desconfie. É promissor, e está longe de pronto.",
          size=19, color=TEXT, align=PP_ALIGN.CENTER, space_after=0, first=True)

    notes(s, "Não pule este slide por falta de tempo — corte outro. "
             "Ele é o que separa a sua palestra das outras vinte sobre IA no evento. "
             "Plateia técnica perdoa limitação declarada e desconfia de solução perfeita.")


# ===========================================================================
# 37 — O que muda
# ===========================================================================
def slide_takeaway():
    s = blank(prs)
    plain.append(s)

    box(s, 0, 0, Inches(0.16), H, fill=ACCENT_DEEP, outline=None, radius=False)

    f = textbox(s, Inches(1.15), Inches(1.4), Inches(11), Inches(0.45))
    write(f, "PARA LEVAR PARA CASA", size=15, color=ACCENT, bold=True, space_after=0, first=True)

    lines = [
        ("O gargalo não é escrever código.", WHITE),
        ("É decidir o que escrever, coordenar quem escreve, e verificar.", MUTED),
    ]

    f = textbox(s, Inches(1.15), Inches(2.0), Inches(11.2), Inches(1.6))
    write(f, lines[0][0], size=40, color=lines[0][1], bold=True, space_after=10, first=True)
    write(f, lines[1][0], size=26, color=lines[1][1], space_after=0)

    rule(s, Inches(4.0), color=BORDER, left=Inches(1.15), width=Inches(11.0))

    points = [
        "O modelo produz. O programa verifica.",
        "Restrição é o que faz paralelismo funcionar — não inteligência.",
        "As regras da sua empresa são arquivo versionado, não prompt escondido.",
        "O merge continua sendo humano. Isso não é limitação, é o desenho.",
    ]

    f = textbox(s, Inches(1.15), Inches(4.35), Inches(11.0), Inches(2.0))
    for index, point in enumerate(points):
        rich(f, [("▸  ", ACCENT, True), (point, TEXT)], size=20, space_after=14, first=(index == 0))

    notes(s, "Quatro frases, trinta segundos. Diga devagar e faça pausa entre elas. "
             "Depois passe direto para o slide de perguntas — não encerre duas vezes.")


# ===========================================================================
# 38 — Perguntas
# ===========================================================================
def slide_questions():
    s = blank(prs)
    plain.append(s)

    box(s, 0, 0, Inches(0.16), H, fill=VIOLET, outline=None, radius=False)

    f = textbox(s, Inches(1.15), Inches(1.85), Inches(11.2), Inches(1.5))
    write(f, "Perguntas", size=64, color=WHITE, bold=True, space_after=8, first=True)
    write(f, "e depois um café, se você quiser continuar a conversa", size=24, color=MUTED, space_after=0)

    rule(s, Inches(4.15), color=BORDER, left=Inches(1.15), width=Inches(11.0))

    f = textbox(s, Inches(1.15), Inches(4.5), Inches(11.0), Inches(1.6))
    write(f, "Todo o código, as regras, o runbook e a transcrição de exemplo:", size=17, color=MUTED, space_after=12, first=True)
    write(f, "github.com/magoolation/tdc-sp-2026-agent-squad", size=27, color=ACCENT, bold=True, font=MONO, space_after=18)

    f = textbox(s, Inches(1.15), Inches(6.2), Inches(11.0), Inches(0.5))
    rich(f, [("Alexandre Costa", TEXT, True), ("     ·     ", DIM),
             ("linkedin.com/in/magoolation", MUTED), ("     ·     ", DIM),
             ("@magoolation", MUTED)], size=17, first=True)

    notes(s, "Deixe este slide no telão durante todas as perguntas — o link fica visível. "
             "PERGUNTAS QUE VÃO VIR, e as respostas curtas:\n\n"
             "• 'Quanto custa?' — Medi tempo, não dinheiro; uma execução de 40 min usa 6 agentes. "
             "Seja honesto que não mediu.\n"
             "• 'Funciona em legado?' — Validei greenfield e mudanças pequenas. Em legado o intake lê o "
             "repositório primeiro, mas não testei em escala.\n"
             "• 'Substitui desenvolvedor?' — Não. Move o trabalho de escrever para especificar e revisar. "
             "E o merge continua humano.\n"
             "• 'Por que não CrewAI/AutoGen/LangGraph?' — A escolha aqui foi .NET + Agent Framework porque "
             "era o contexto; o desenho (gate determinístico, regra de conflito, worktree) é independente de framework.\n"
             "• 'E se o agente fizer besteira?' — A política de permissões impede push, PR e comandos de "
             "infraestrutura. E nada entra sem merge humano.\n"
             "• 'Posso usar no meu trabalho?' — MIT, está tudo no repositório.")


# ===========================================================================
# Build
# ===========================================================================
slide_title()
slide_speaker()
slide_agenda()
slide_promise()
slide_why_hard()
slide_pipeline()
slide_stack()
slide_agents()
slide_decision_gate()
slide_decision_conflict()

slide_demo(1, "Pré-requisitos, com diagnóstico útil",
           "Nada de 'algo deu errado'. Cada item reprovado vem com o comando que corrige.",
           [("> squad doctor --probe-models", ACCENT),
            ("", MUTED),
            ("  ✔  .NET SDK                   10.0.401", SUCCESS),
            ("  ✔  GitHub CLI                 2.101.0", SUCCESS),
            ("  ✖  GitHub token scopes        'repo'", DANGER),
            ("     → gh auth refresh -h github.com -s workflow", WARNING),
            ("  ✔  Microsoft Foundry          gpt-5.5 · 36946 ms · \"Olá, TDC!\"", SUCCESS)],
           ["O doctor faz uma chamada REAL a cada modelo — não só testa configuração",
            "Cada falha traz o comando exato, não uma mensagem genérica"],
           "3 min")

slide_customization()
slide_rules_executable()

slide_demo(2, "Da reunião ao requisito",
           "A transcrição de uma reunião de levantamento, com as patologias de uma reunião real.",
           [("> squad run --transcript samples/meeting-transcripts/kickoff-catalogo.md", ACCENT),
            ("", MUTED),
            ("  ◆ transcript-analyst   5 decisões · 3 pendências · 1 suposição não compartilhada", VIOLET),
            ("  ◆ requirements-analyst 21 requisitos, 7 suposições declaradas", VIOLET),
            ("  ◆ requirements-analyst 4 perguntas para o humano", WARNING)],
           ["O teste de fogo: o agente lista o p95 como PENDÊNCIA, ou registra como requisito?",
            "O gabarito está no fim do próprio arquivo da transcrição"],
           "7 min")

slide_transcript_insight()
slide_hitl()
slide_validator_critic()

slide_demo(3, "Cinco agentes, três ondas",
           "Cada um em seu worktree, com o gate determinístico decidindo no fim de cada um.",
           [("> squad run --request \"...\" --parallel 3", ACCENT),
            ("", MUTED),
            ("  ◆ Onda 1/3: 1 agente     →  Gate aprovado  →  PR #11", SUCCESS),
            ("  ◆ Onda 2/3: 2 agentes    →  Gate aprovado  →  PR #12, #13", SUCCESS),
            ("  ◆ Onda 3/3: 2 agentes    →  Gate aprovado  →  PR #14, #15", SUCCESS),
            ("  ◆ 5/5 aprovados no gate · 40,3 min", VIOLET)],
           ["Abra o dashboard do Aspire: spans GenAI ao vivo, com tokens e latência",
            "Se o gate reprovar, PARE e mostre o diagnóstico voltando para o agente"],
           "8 min")

slide_gate()
slide_security()

slide_demo(4, "Os pull requests",
           "Dois níveis: os PRs por item são as unidades de revisão. O PR de entrega é o portão.",
           [("> gh pr list --repo magoolation/squad-demo-catalogo", ACCENT),
            ("", MUTED),
            ("  #27  [agent-generated]  feat: catálogo   agent/run-... → main", VIOLET),
            ("  #15  [area:api]   feat: exponha get products com logs e testes", TEXT),
            ("  #13  [area:core]  feat: implemente validação filtros e paginação", TEXT),
            ("  #11  [area:infra] build: crie a base .net e os contratos", TEXT)],
           ["Abra um PR de item: tabela do gate, apontamento citando TST-004 com arquivo:linha, procedência",
            "Depois abra o #27: o que entrou, o que NÃO entrou, e 'O merge é seu.'"],
           "3 min")

slide_findings_intro()

finding(1, "Os nomes mudaram, e o seu código de blog post quebrou",
        "Azure AI Foundry virou Microsoft Foundry. O papel \"Azure AI User\" virou \"Foundry User\". "
        "Microsoft.Agents.AI.AzureAI foi abandonado. CreateAIAgent virou AsAIAgent. AgentThread virou AgentSession.",
        "Amarre por GUID, não por nome. Trate a documentação como sugestão e o compilador como autoridade — "
        "encontrei erro em página de doc atualizada dois dias antes.",
        [("Aspire.Hosting.Azure.AIFoundry   →   Aspire.Hosting.Foundry", WARNING),
         ("AddAzureAIFoundry(...)           →   AddFoundry(...)", WARNING),
         ("Microsoft.Agents.AI.AzureAI      →   Microsoft.Agents.AI.Foundry", WARNING)])

finding(2, "Modelo de raciocínio rejeita temperature",
        "gpt-5.5 responde HTTP 400: \"Unsupported parameter: 'temperature' is not supported with this model\". "
        "gpt-5.4-mini, no mesmo projeto, aceita normalmente.",
        "Enviar temperature incondicionalmente acopla a escolha do modelo à configuração de sampling. "
        "Trocar por um modelo melhor quebra o pipeline na primeira chamada. Saída estruturada com schema "
        "já é o que restringe esses agentes.",
        [("HTTP 400 (invalid_request_error)  Parameter: temperature", DANGER)])

finding(3, "Cota e Marketplace: duas paredes antes do runtime",
        "Uma assinatura pode estar habilitada para um modelo e ter cota ZERO — o deployment falha no preflight. "
        "E modelos parceiros (Anthropic, xAI, Mistral) são compra de Marketplace: exigem modelProviderData "
        "e um método de pagamento válido.",
        "Rode az cognitiveservices usage list ANTES de escrever o Bicep. E antes de ensaiar a palestra. "
        "Modelos OpenAI são vendidos direto pela Azure e não têm nenhum dos dois requisitos.",
        [("InsufficientQuota: requires 50 new capacity, available 0", DANGER),
         ("Marketplace purchase eligibility check failed: no valid payment method", DANGER)])

finding(4, "git worktree no Windows não é atômico",
        "Quando worktree remove não consegue apagar o diretório — e um processo apenas tê-lo como diretório atual "
        "já basta — ele JÁ apagou os arquivos e JÁ desregistrou o worktree. Retry dá \"is not a working tree\".",
        "A recuperação é sistema de arquivos, não git. E derrube os build servers antes: nós do MSBuild seguram "
        "assemblies de analisador por 15 minutos — medido com PIDs estáveis.",
        [("error: failed to delete '...': Permission denied   (exit 255)", DANGER),
         ("fatal: '...' is not a working tree                  (exit 128)", DANGER)])

finding(5, "NUGET_SCRATCH dividido corrompe o cache para sempre",
        "Os locks entre processos do NuGet vivem em %TEMP%\\NuGetScratch, não na pasta de pacotes. "
        "Com pasta compartilhada e scratch por worker: 24 de 24 restores concorrentes falharam.",
        "E a corrupção é PERMANENTE: todo pacote fica com o marcador de conclusão mas sem o .nuspec, "
        "e restores seriais posteriores falham até limpar o cache na mão. Um scratch para todos: 0 falhas em 24.",
        [("NU5037: The package is missing the required nuspec file", DANGER)])

finding(6, "O gate culpava o agente por um bug meu",
        "--artifacts-path realoca obj/ além de bin/. Eu passava no build mas não no restore. "
        "O build falhava com NETSDK1004, e a falha se parecia exatamente com código quebrado.",
        "Um gate que culpa a parte errada é pior do que gate nenhum: fez um agente correto parecer incompetente "
        "e queimou as tentativas de reparo consertando algo que nunca esteve errado. "
        "Rodei o gate à mão no código dele: passava inteiro, com 6 testes.",
        [("error NETSDK1004: Assets file 'obj/project.assets.json' not found", DANGER)],
        symptom_color=VIOLET)

finding(7, "As ondas não enxergavam umas às outras",
        "Todo worktree ramificava de main e todo PR mirava main. Como o merge é humano e os PRs ficam abertos, "
        "a onda 2 nunca via o trabalho da onda 1.",
        "Cada agente reconstruía a fundação do zero, e os cinco PRs conflitariam entre si no merge. "
        "A correção: um branch de integração por execução. As ondas acumulam nele, os PRs miram nele, "
        "e um único merge humano entrega o conjunto.",
        [("PR da onda 3 continha a solução inteira, não só a sua mudança", DANGER)])

finding(8, "A correção do achado 7 fechou PRs que ninguém revisou",
        "Integrar uma onda faz os commits dos PRs dela virarem alcançáveis pela base. "
        "O GitHub então marca esses PRs como MERGED sozinho. Três deles, sem revisão humana nenhuma.",
        "É comportamento normal de stacked PRs, não bug — mas a execução terminava sem nada para aprovar, "
        "e isso contradiz a regra que está no slide anterior. Faltava a última peça: um PR de entrega, "
        "do branch de integração para main. Os PRs por item são unidades de REVISÃO; o de entrega é o PORTÃO.",
        [("#24 #25 #26  MERGED  — nenhum humano abriu esses", DANGER),
         ("#27  DRAFT  agent/run-... → main  — este é o portão", SUCCESS)],
        symptom_color=VIOLET)

slide_pattern()
slide_how_order()
slide_how_nonnegotiable()
slide_limits()
slide_takeaway()
slide_questions()

# --- footers ---------------------------------------------------------------
from theme import footer  # noqa: E402

total = len(prs.slides._sldIdLst)
for index, slide in enumerate(prs.slides, start=1):
    if slide not in plain:
        footer(slide, index, total)

out = Path(sys.argv[1] if len(sys.argv) > 1 else "Agent-Squad-TDC-SP-2026.pptx")
out.parent.mkdir(parents=True, exist_ok=True)
prs.save(out)
print(f"ok: {out}  ({total} slides)")
