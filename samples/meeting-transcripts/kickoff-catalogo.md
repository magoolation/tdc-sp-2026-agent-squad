# Transcrição — Kickoff: API de Catálogo de Produtos

**Data:** 18/09/2026 · **Duração:** 34 min · **Local:** Teams (transcrição automática, revisada)

**Participantes**

| | |
|---|---|
| **Renata Alves** | Gerente de Produto — Marketplace |
| **Caio Monteiro** | Tech Lead — Plataforma |
| **Juliana Prado** | Engenheira Sênior — Backend |
| **Marcos Tanaka** | Head de Operações (entrou aos 12 min) |
| **Bruno Sato** | Engenheiro de Dados |

> **Nota para quem for usar esta transcrição na demo:** ela foi escrita de propósito com
> as patologias de uma reunião real — um requisito que ninguém quantificou, uma
> divergência que ficou sem resolução, um "obviamente" que não é óbvio, um escopo que
> cresceu no meio da conversa e uma decisão tomada por quem entrou depois. O valor da
> demonstração não é o agente resumir a reunião. É ele **separar o que foi decidido do que
> só pareceu decidido**.

---

**[00:14] Renata:** Obrigada por virem. O contexto é o seguinte: hoje o time de
marketplace consulta o catálogo direto no banco do monolito. Toda vez que a gente faz uma
promoção, o banco senta. Precisamos de uma API de catálogo separada.

**[00:41] Caio:** Separada como? API nova na frente do mesmo banco, ou base própria?

**[00:52] Renata:** Não sei responder isso, é decisão de vocês. Do meu lado o que importa
é que a busca não caia quando tiver pico.

**[01:10] Caio:** Ok. Então o requisito real é isolamento de carga de leitura, não
necessariamente um banco novo.

**[01:22] Renata:** Isso. E paginação. Hoje a tela puxa tudo e o front corta no cliente,
o que é ridículo.

**[01:35] Juliana:** Paginação por offset ou cursor?

**[01:39] Renata:** Qual a diferença pro usuário?

**[01:44] Juliana:** Offset é mais simples e deixa você pular pra página 40 direto. Cursor
é estável quando o catálogo muda enquanto o usuário navega — não repete nem pula item —
mas você só anda pra frente e pra trás.

**[02:03] Renata:** A tela tem paginação numerada hoje. As pessoas usam.

**[02:09] Juliana:** Então offset. Mas aviso que com catálogo mudando durante a navegação
vai ter item repetido eventualmente.

**[02:19] Renata:** Tudo bem, ninguém reclamou disso até hoje.

> **Decisão:** paginação por offset, com numeração de páginas. Renata (dona do produto).

**[02:31] Caio:** Busca é por quê? Nome só, ou tem filtro?

**[02:38] Renata:** Nome, categoria, faixa de preço. E tem que ser rápido.

**[02:44] Caio:** "Rápido" quanto?

**[02:47] Renata:** Rápido. Instantâneo. O usuário não pode sentir.

**[02:53] Caio:** Isso não me dá um número pra projetar.

**[02:58] Renata:** Não sei te dar número. Só sei que hoje demora uns três, quatro
segundos e é inaceitável.

**[03:10] Juliana:** Dá pra trabalhar com p95 abaixo de 300 ms? É o que a gente consegue
com índice decente sem cache.

**[03:20] Renata:** Se for isso eu assino embaixo.

**[03:24] Caio:** Anota como meta, não como SLA. A gente não tem baseline ainda.

> **Aparente decisão, na verdade ambígua:** "p95 < 300 ms" foi proposto pela Juliana e
> aceito pela Renata, mas o Caio explicitamente reclassificou como meta. Ninguém
> reconciliou. Duas pessoas saíram da reunião com entendimentos diferentes.

**[03:48] Bruno:** Vocês vão querer o catálogo replicado pro meu lado também? Porque se
for criar base nova eu preciso saber.

**[03:59] Caio:** Ainda não decidimos se tem base nova.

**[04:05] Bruno:** Ok, mas se tiver, me avisa antes. Eu não quero descobrir isso em
produção de novo.

**[04:14] Caio:** Justo.

**[04:20] Juliana:** Uma coisa — o catálogo tem produto inativo, produto de vendedor
suspenso, produto sem estoque. A API retorna tudo isso?

**[04:33] Renata:** Não. Só o que dá pra comprar.

**[04:38] Juliana:** "Dá pra comprar" é: ativo, vendedor ativo e estoque maior que zero?

**[04:45] Renata:** Acho que sim.

**[04:48] Juliana:** "Acho que sim" ou "sim"? Porque tem produto sob encomenda que fica
com estoque zero e mesmo assim vende.

**[04:58] Renata:** Ah. Verdade. Esses tem que aparecer.

**[05:04] Juliana:** Então a regra não é estoque maior que zero. É estoque maior que zero
**ou** marcado como sob encomenda.

**[05:12] Renata:** Isso.

> **Decisão:** visível = produto ativo **e** vendedor ativo **e** (estoque > 0 **ou**
> sob encomenda). Renata + Juliana.

**[05:30] Caio:** Autenticação?

**[05:33] Renata:** É público, é o catálogo.

**[05:37] Caio:** Público mesmo, ou público pra quem tem a chave do app?

**[05:43] Renata:** Hmm. O app mobile consome, o site consome... e tem parceiro que puxa
também.

**[05:52] Caio:** Então não é público. Parceiro é outra coisa, tem contrato, tem limite.

**[05:59] Renata:** Não pensei nisso.

**[06:04] Caio:** Vamos deixar assim: nesta primeira versão, leitura pública sem
autenticação, mas com rate limit por IP. Acesso de parceiro fica pra depois, com API key.

**[06:18] Renata:** Fechado.

> **Decisão:** v1 sem autenticação, com rate limit por IP. Integração de parceiros fica
> explicitamente fora de escopo. Caio + Renata.

**[06:40] Juliana:** Volume? Quantos produtos, quantas requisições?

**[06:46] Renata:** Uns 200 mil produtos. Requisição eu não sei.

**[06:53] Bruno:** Pelo log do monolito, a busca de catálogo dá uns 40 por segundo em dia
normal. Em Black Friday já vi 600.

**[07:08] Caio:** Ok, isso é um número. Projeta pra 600, não pra 40.

**[07:15] Juliana:** Com 600 rps e p95 de 300 ms sem cache, eu duvido. Vai precisar de
cache.

**[07:24] Caio:** Cache a gente resolve depois de medir. Não quero colocar Redis antes de
provar que precisa.

**[07:33] Juliana:** Concordo, mas então a meta de 300 ms na Black Friday não se sustenta
sem cache.

**[07:41] Caio:** Em Black Friday a gente aceita degradar.

**[07:45] Renata:** Espera, degradar como? Não pode ficar lento justo na Black Friday.

**[07:52] Caio:** Renata, é exatamente aí que sempre fica lento.

**[07:56] Renata:** Eu sei, mas é aí que a gente vende.

**[08:02] Caio:** Então precisamos de cache desde o começo, e você aceita que o resultado
da busca pode estar até alguns segundos desatualizado.

**[08:12] Renata:** Quantos segundos?

**[08:15] Caio:** Trinta? Sessenta?

**[08:19] Renata:** Sessenta segundos mostrando um produto que acabou de esgotar... o
cliente clica, vai pro carrinho e toma erro. Isso gera ticket.

**[08:31] Caio:** Então cinco segundos. Mas aí o cache ajuda muito menos.

**[08:38] Renata:** Não sei. Deixa eu pensar.

**[08:42] Caio:** Vamos deixar em aberto e decidir com número na mão.

> **PENDÊNCIA EXPLÍCITA:** estratégia de cache e janela de desatualização aceitável.
> Ninguém decidiu. Esta é a pendência mais cara da reunião: ela muda a arquitetura.

**[09:05] Juliana:** Outra coisa. Hoje o preço vem com promoção aplicada ou sem?

**[09:12] Renata:** Com.

**[09:15] Juliana:** Então a API de catálogo precisa falar com o serviço de promoções em
toda requisição.

**[09:23] Renata:** Precisa?

**[09:26] Juliana:** Se o preço tem que estar promocional e a promoção muda, sim.

**[09:33] Caio:** Ou a promoção publica o preço final pro catálogo e a gente lê pronto.

**[09:40] Juliana:** Aí inverte a dependência. Prefiro.

**[09:44] Caio:** Eu também, mas isso depende do time de promoções topar.

**[09:50] Renata:** Eu falo com eles.

> **PENDÊNCIA:** quem calcula o preço promocional. Depende de um time que não estava na
> reunião. Renata ficou de encaminhar.

**[10:20] Juliana:** Vou perguntar o óbvio: isso é .NET, né?

**[10:25] Caio:** Obviamente. Tudo da plataforma é .NET.

**[10:29] Juliana:** Tá, mas o time de busca escreveu o serviço novo deles em Go.

**[10:35] Caio:** Aquilo foi exceção.

**[10:38] Juliana:** Foi exceção ou virou precedente? Porque eu ouvi que o próximo
serviço deles também vai ser Go.

**[10:47] Caio:** ...vou confirmar.

> **SUPOSIÇÃO NÃO COMPARTILHADA:** "obviamente .NET". O Caio trata como óbvio; a Juliana
> tem evidência em contrário. Este é exatamente o tipo de coisa que some do resumo da
> reunião e reaparece como retrabalho três sprints depois.

**[11:15] Renata:** Prazo: a gente queria antes da Black Friday. Isso dá o quê, dez
semanas?

**[11:24] Caio:** Dez semanas dá, se o escopo for o que a gente falou até agora.

**[11:30] Renata:** Ah, e já que vamos mexer nisso — dá pra incluir recomendação de
produto relacionado?

**[11:38] Caio:** Não.

**[11:39] Renata:** Só perguntei.

**[11:41] Caio:** E a resposta continua não. Recomendação é um produto inteiro, não um
campo a mais no endpoint.

> **Fora de escopo, explicitamente:** recomendação de produtos relacionados.

**[12:05]** *Marcos entra.*

**[12:10] Marcos:** Desculpa o atraso. Já decidiram alguma coisa que me afete?

**[12:16] Caio:** Talvez. Estamos falando de uma API de catálogo separada, possivelmente
com base própria.

**[12:24] Marcos:** Base própria eu preciso saber com antecedência, porque replicação eu
não tenho gente pra operar esse trimestre.

**[12:34] Caio:** Então talvez a gente comece com réplica de leitura do banco atual.

**[12:41] Marcos:** Isso eu consigo. Réplica de leitura já existe, está subutilizada.

**[12:48] Caio:** Perfeito. Fase 1: API nova apontando para a réplica de leitura que já
existe. Base própria fica pra fase 2, se precisar.

**[12:58] Renata:** Por mim tudo bem.

> **Decisão, tomada por quem chegou atrasado:** fase 1 usa a réplica de leitura existente.
> Vale reparar que a pergunta do Caio aos 00:41 ficou 12 minutos em aberto e foi resolvida
> por uma restrição operacional, não por uma escolha de arquitetura.

**[13:30] Marcos:** Observabilidade vocês vão colocar desde o começo, né? Porque se cair
na Black Friday e eu não tiver métrica, quem acorda às 3 da manhã sou eu.

**[13:44] Caio:** Sim. Métrica de latência, taxa de erro e taxa de acerto do cache — se
tiver cache.

**[13:53] Marcos:** E alerta. Métrica sem alerta é enfeite.

**[13:58] Caio:** E alerta.

> **Decisão:** observabilidade (latência, erro, cache hit) e alertas fazem parte da fase 1.
> Marcos.

**[14:30] Bruno:** Só pra registrar: se for réplica de leitura, o lag de replicação entra
na conta da desatualização que vocês estavam discutindo.

**[14:42] Caio:** Boa. Anotado.

**[14:46] Juliana:** Isso muda a pergunta do cache, inclusive. Se a réplica já está alguns
segundos atrás, discutir cache de cinco segundos é discussão errada.

**[14:58] Caio:** Concordo. Vamos medir o lag primeiro.

**[15:04] Renata:** Então a gente fecha assim?

**[15:07] Caio:** Fechamos o suficiente pra começar. Falta decidir cache e preço
promocional.

**[15:15] Renata:** Combinado. Eu levo o preço promocional, vocês medem o lag.

**[15:22] Caio:** Fechado.

**[15:25] Renata:** Obrigada, gente.

*[Fim da transcrição]*

---

## O que um bom analista deve extrair daqui

Esta seção **não** faz parte da transcrição e não é enviada ao agente. Ela existe para
você conferir, ao vivo, se o agente acertou.

**Decisões reais (5)**
1. Paginação por offset, com numeração de páginas.
2. Visível = ativo **e** vendedor ativo **e** (estoque > 0 **ou** sob encomenda).
3. v1 sem autenticação, com rate limit por IP.
4. Fase 1 usa a réplica de leitura existente; base própria fica para a fase 2.
5. Observabilidade com latência, erro, cache hit — e alertas — na fase 1.

**Pendências não resolvidas (3)**
1. Estratégia de cache e janela de desatualização aceitável. *Explícita.*
2. Quem calcula o preço promocional — depende de um time ausente. *Explícita.*
3. **p95 de 300 ms: meta ou SLA?** A Juliana propôs, a Renata aceitou, o Caio
   reclassificou como meta e ninguém fechou. *Implícita — a mais perigosa das três,
   porque todo mundo saiu achando que estava combinado.*

**Suposição tratada como óbvia (1)**
- "Obviamente .NET." Há evidência de precedente em contrário e o próprio Caio ficou de
  confirmar.

**Restrições**
- Prazo: antes da Black Friday (~10 semanas).
- 200 mil produtos; 40 rps normal, 600 rps em pico.
- Sem gente para operar replicação nova neste trimestre.

**Fora de escopo, dito explicitamente**
- Recomendação de produtos relacionados.
- Integração de parceiros com API key.

**O teste de fogo da demo:** o agente listou a ambiguidade do p95 como pendência, ou
registrou "p95 < 300 ms" como requisito acordado? A segunda resposta é a que um resumo
comum produz — e é errada.
