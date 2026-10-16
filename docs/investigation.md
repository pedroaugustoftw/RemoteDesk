# Investigação técnica e visual

Data: 14/10/2026; revisão visual: 15/10/2026. Alvo autorizado: `C:\Program Files (x86)\AnyDesk`. Nenhum arquivo instalado foi modificado. Não houve extração de credenciais, recuperação de chaves privadas, quebra de proteção ou reprodução de protocolo proprietário.

## Evidências e cobertura

O MCP REA 6.5.0 esteve conectado ao cliente Codex. `open_binary` identificou PE x86 e vinculou Ghidra 12.1.4. A inspeção do executável foi estática, com projeto temporário e arquivo original imutável. As capturas de janela foram feitas separadamente pelo Windows; não são evidências de execução fornecidas pelo REA.

| Observação | Evidência / origem | Limite |
|---|---|---|
| Executável principal: AnyDesk.exe, 8.351.672 bytes, SHA256 b3e2fff740c95f16af5310242bd2af0de71002d2c1ef3a8ff3802a1ef7fa5fb4 | MCP open_binary e recursos PE | Identidade do arquivo instalado nesta data |
| PE32 x86 nativo, base 0x400000, 8 regiões, 54 procedimentos e 29 strings definidos pelo analisador | ev_0346ec2bf4392eadd89ba426c8c970388ab443304fe90a4c7dcdf5b014fe78ab | Inventário estático definido pelo Ghidra; não é cobertura de toda lógica em execução |
| Versão 9.7.13 / produto 9.7, manifesto Win32 asInvoker e dependência Common-Controls 6 | ev_bdbaa536b7cad9689d0ff54dc87ed2c91d5aadee32dd2c3e481621a2e992576a | Declarações do arquivo, não prova de carregamento |
| 11 recursos PE: sete imagens de ícone, dois grupos, versão e manifesto | ev_d3053b264708b52e3b0c7ca9d1b85b4bd917d4f707a13d2b50af96d54895414f | Inventário completo do diretório PE; outros conteúdos internos não decodificados |
| gcapi.dll: 394.240 bytes, PE32 x86, 2 recursos | ev_4df73cfa5b088b4d1603770cae69a2b6f74e7796731e8e86c19a71b4b7222533 | Existência não comprova papel ou carregamento |
| Processos observados com e sem janela, em WOW64 | PowerShell Get-Process, módulos acessíveis | Processos de serviço não completamente acessíveis; lista 64→32 parcial |
| Inventário de símbolos sem símbolos externos definidos | ev_93e00b3f8ec3c166e214dce19a5a15fa2993dba3563aa31791720a06a8c05112 | Não prova ausência de bibliotecas carregadas dinamicamente |

As regiões `.itext` e `.data` têm grandes intervalos em memória e o inventário recuperado é reduzido. Empacotamento ou transformação de código é uma hipótese, não uma conclusão comprovada. Não se tentou desempacotar ou contornar mecanismos de proteção. Linguagem fonte, framework gráfico interno, IPC, codec de tela, protocolo de sessão, descoberta, STUN/TURN e criptografia proprietária continuam **não determinados**. Não são inferidos a partir de nomes.

O produto próprio utiliza WPF/.NET, RSA-PSS, DPAPI, TLS/WSS e JPEG sobre transporte original. Essas são decisões de implementação independente, não conclusões sobre o aplicativo analisado.

## Observação visual

Capturas preservadas: `evidence/reference-window.png`, `evidence/reference-visible.png` e `evidence/reference-settings.png`. O serviço Computer Use não expôs seu pipe nativo nesta sessão, impossibilitando navegação automática. O usuário abriu manualmente as Configurações. Capturas somente de leitura permitiram observar a janela e o desktop visível. A referência foi observada a 1280 pixels de largura, com conteúdo que se prolonga além da borda direita; componentes fora dessa região não foram inventariados.

### Tela inicial

Medidas aproximadas em pixels da captura:

| Região | Posição e proporção observadas |
|---|---|
| Barra de abas | y0–47, fundo #252323; identidade esquerda, aba de nova sessão a partir de x158 |
| Endereço | y48–94, fundo #131313, indicador verde em x14–36, campo a partir de x49 |
| Faixa de destaque | y95–154, vermelho próximo a #FA4035, altura 60 |
| Identificação local | y155–285; legenda à esquerda do ID; número grande vermelho próximo a #F4433B |
| Navegação | aproximadamente y318; itens Notícias, Favoritos, Sessões Recentes, Encontrados, Convites |
| Área inferior | legenda em y382 e separador y413; cartões a partir de x46, y436, larguras aproximadamente352 e intervalos12 |
| Cores dos cartões | azul escuro, magenta, azul médio e amarelo; artes promocionais originais não reutilizadas |

A interface usa tipografia sem serifa semelhante à Segoe UI. Textos e dimensões foram aproximados visualmente; família e métricas internas exatas não foram extraídas. O RemoteDesk mantém faixas, hierarquia, campo superior, ID destacado, navegação horizontal, tema escuro e cartões planos. Possui um símbolo R original e conteúdo próprio. O banner comercial foi substituído por estado da aplicação. Essa substituição preserva a região, mas não replica conteúdo promocional.

### Configurações

Barra de abas, endereço/breadcrumb e faixa superior continuam no mesmo lugar. Barra lateral observada ocupa x0–390 (~30,5% da largura); conteúdo começa em x461, fundo #131313. Título vermelho no canto esquerdo; categorias e itens empilhados. Painel observado contém Idioma, Tema e Outros, separados por títulos e linhas. Seleção vermelha e indicadores simples.

O RemoteDesk preserva a divisão lateral/conteúdo, títulos, ordem geral e tema. Opções próprias de servidor, qualidade, certificado e identificação têm implementação funcional. Recursos não implementados aparecem desabilitados. Há diferenças deliberadamente registradas: menus não completamente observados, menos opções disponíveis, apenas português e tema escuro. Não se afirma fidelidade completa.

### Telas não observadas

Conteúdo de dispositivos recentes, menus completos, solicitação e autorização original, estados de carregamento, sessão remota original e barra de ferramentas de sessão **não foram observados**. Não havia segundo destino autorizado nem automação funcional. Os diálogos e a janela de sessão do RemoteDesk são componentes originais mínimos necessários para consentimento e funcionamento; seu layout não é apresentado como cópia fiel dessas telas.

## Preservação

Não foram usados ícones, imagens promocionais, textos comerciais, bibliotecas ou código do aplicativo original no cliente, servidor, relay ou instalador RemoteDesk. Referências ao nome do alvo ficam restritas ao material técnico da investigação. O pacote de distribuição contém somente recursos e implementação próprios.

## Revisão das três imagens completas — 15/10/2026

Foram localizados e inspecionados `E:/AnyCraft/interface principal.png`, `E:/AnyCraft/menu lateral.png` e `E:/AnyCraft/configuração aberta.png`. São evidências visuais fornecidas pelo usuário, separadas da análise estática REA. Corrigem a limitação das capturas anteriores: mostram aproximadamente1918pixels de largura. A lateral de390pixels corresponde a cerca de20% dessa largura, não aos30,5% descritos na captura anterior parcial.

O menu fica à direita abaixo da linha de endereço, com aproximadamente516pixels de largura e linhas de44pixels. A tela inicial mostra cinco cartões de aproximadamente352pixels e sessões recentes abaixo. O conteúdo de configurações possui largura útil aproximada825pixels e recuo70pixels após a lateral. A busca lateral e os grupos Aplicativo, Segurança e Sessão foram observados; itens posteriores à borda inferior da captura não foram inferidos.

O menu principal e a organização dos recentes passam a ser considerados observados visualmente. Seus fluxos internos, solicitação, autorização e interface de sessão continuam sem observação no original. Recursos sem implementação no RemoteDesk ficam indisponíveis; não se oferece acesso não supervisionado. Textos promocionais e recursos gráficos proprietários não são usados no produto.

A reinspeção REA confirmou o mesmo SHA256 e a evidência `ev_d3053b264708b52e3b0c7ca9d1b85b4bd917d4f707a13d2b50af96d54895414f`:26entradas e11recursos PE, sem executar ou modificar o original. Esses recursos não recuperam os layouts ou protocolos; a reconstrução visual usa as imagens observadas.
