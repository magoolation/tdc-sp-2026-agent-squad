"""Acessibilidade para os slides gerados.

O python-pptx não expõe texto alternativo, marcação de decorativo nem placeholder de
título, então isto mexe no XML por baixo. São três coisas, e todas valem mais do que
parecem para quem navega com leitor de tela:

1. **Título de slide de verdade.** Leitor de tela e o "Ir para slide" do PowerPoint leem o
   placeholder de título. Um deck feito de caixas de texto soltas — que é o caso deste —
   não tem nenhum, e o resultado é uma lista de "Slide 12, Slide 13" sem nome.

2. **Decorativo marcado como decorativo.** O retângulo de fundo, as réguas e as setas não
   carregam informação. Sem marcação, o leitor de tela anuncia cada um como "Retângulo 3",
   e a pessoa percorre cinco objetos vazios antes de chegar ao conteúdo.

3. **Texto alternativo no que carrega informação.** Para forma com texto, o leitor já lê o
   texto; o alt importa nas composições, onde o sentido está no conjunto e não em cada
   peça isolada.

A ordem de leitura é a ordem do `spTree`, que aqui já é a ordem em que as formas são
desenhadas: fundo primeiro, depois o conteúdo de cima para baixo. Marcar o fundo como
decorativo resolve o resto.
"""

from pptx.oxml.ns import qn

# A extensão que o PowerPoint usa para "marcar como decorativa".
_DECORATIVE_URI = "{C183D7F6-B498-43B3-948B-1728B52AA6E4}"
_DECORATIVE_NS = "http://schemas.microsoft.com/office/drawing/2017/decorative"


def _non_visual(shape):
    """Devolve o elemento cNvPr da forma, que é onde nome e alt text moram."""
    element = shape._element
    for tag in ("p:nvSpPr", "p:nvPicPr", "p:nvGrpSpPr", "p:nvCxnSpPr", "p:nvGraphicFramePr"):
        holder = element.find(qn(tag))
        if holder is not None:
            return holder.find(qn("p:cNvPr"))
    return None


def set_alt(shape, description, name=None):
    """Põe texto alternativo na forma. `name` renomeia o objeto na lista de seleção."""
    cnv = _non_visual(shape)
    if cnv is None:
        return

    # O PowerPoint lê 'descr' como texto alternativo e 'title' como título do objeto.
    cnv.set("descr", " ".join(description.split()))
    if name:
        cnv.set("name", name)


def mark_decorative(shape):
    """Marca a forma como decorativa: o leitor de tela pula."""
    cnv = _non_visual(shape)
    if cnv is None:
        return

    cnv.set("descr", "")

    ext_lst = cnv.find(qn("a:extLst"))
    if ext_lst is None:
        ext_lst = cnv.makeelement(qn("a:extLst"), {})
        cnv.append(ext_lst)

    for existing in ext_lst.findall(qn("a:ext")):
        if existing.get("uri") == _DECORATIVE_URI:
            return

    ext = ext_lst.makeelement(qn("a:ext"), {"uri": _DECORATIVE_URI})
    decorative = ext.makeelement("{%s}decorative" % _DECORATIVE_NS, {"val": "1"})
    ext.append(decorative)
    ext_lst.append(ext)


TITLE_MARKER = "__a11y_title__"


def mark_as_title(shape):
    """Sinaliza que esta forma é o título do slide. A promoção acontece no finalize."""
    cnv = _non_visual(shape)
    if cnv is not None:
        cnv.set("name", TITLE_MARKER)


def _promote_title(slide):
    """Transforma a forma marcada no placeholder de título do slide.

    Sem isto o slide não tem nome em lugar nenhum: nem no painel de miniaturas, nem na
    navegação por slide, nem no modo de estrutura de tópicos.
    """
    for shape in slide.shapes:
        cnv = _non_visual(shape)
        if cnv is None or cnv.get("name") != TITLE_MARKER:
            continue

        text = shape.text_frame.text.strip().splitlines()
        cnv.set("name", "Title 1")
        cnv.set("descr", "")

        nv_sp_pr = shape._element.find(qn("p:nvSpPr"))
        if nv_sp_pr is None:
            return text[0] if text else ""

        nv_pr = nv_sp_pr.find(qn("p:nvPr"))
        if nv_pr is None:
            nv_pr = nv_sp_pr.makeelement(qn("p:nvPr"), {})
            nv_sp_pr.append(nv_pr)

        if nv_pr.find(qn("p:ph")) is None:
            nv_pr.append(nv_pr.makeelement(qn("p:ph"), {"type": "title"}))

        return text[0] if text else ""

    return ""


def finalize(prs, descriptions=None):
    """Passa o deck inteiro: promove títulos, marca decorativo, preenche alt text.

    `descriptions` mapeia número do slide para uma descrição da composição — use para os
    slides cujo sentido está no arranjo (pipeline, fluxo do gate) e não nas peças soltas.

    Devolve a lista de slides que ficaram sem título, para o build reclamar em vez de
    deixar passar em silêncio.
    """
    descriptions = descriptions or {}
    untitled = []

    for index, slide in enumerate(prs.slides, 1):
        title = _promote_title(slide)

        if not title:
            untitled.append(index)

        if index in descriptions:
            # A descrição da composição vai no primeiro objeto não decorativo, que é onde
            # o leitor de tela chega depois de pular o fundo.
            for shape in slide.shapes:
                if shape.has_text_frame and shape.text_frame.text.strip():
                    set_alt(shape, descriptions[index] + " " + shape.text_frame.text)
                    break

        for shape in slide.shapes:
            cnv = _non_visual(shape)
            if cnv is None or cnv.get("name") == "Title 1":
                continue

            # Forma com texto não recebe alt: o leitor de tela já lê o texto, e um alt
            # idêntico faz parte dos leitores anunciarem a mesma frase duas vezes. Alt
            # existe para o que o texto não diz — por isso só as composições recebem.
            if not (shape.has_text_frame and shape.text_frame.text.strip()):
                mark_decorative(shape)

    return untitled
