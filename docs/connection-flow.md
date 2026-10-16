# Conexão, ponteiro e modo administrador

Atualize o servidor e os dois clientes. O destino anuncia ao servidor apenas se o acesso por senha está habilitado, sem enviar senha ou verificador. Ao clicar Conectar:

- Sem senha habilitada no destino: a janela do solicitante mostra Aguardando autorização. O destino aceita ou recusa; a recusa é mostrada explicitamente ao solicitante.
- Com senha habilitada: o solicitante recebe imediatamente o campo mascarado e a caixa Salvar senha. Uma senha já salva é usada automaticamente. A senha continua cifrada para a chave do destino; o servidor não valida nem conhece a senha.
- A janela permite cancelar e limpa o campo após uso. A senha marcada para salvar só é persistida após autenticação bem-sucedida, com DPAPI do usuário Windows.

A barra flutuante aparece somente no PC acessado. Quem acessa tem os botões Encerrar, transferência autorizada e Qualidade/Velocidade integrados na própria janela da sessão. O ponteiro do solicitante fica azul quando há permissão de controle; no destino, uma indicação azul acompanha os movimentos remotos autorizados, sem interceptar cliques. Ao encerrar, a indicação é removida.

## Administrador

No PC acessado, abra Configurações → Acesso → Executar como administrador. Confirme a reinicialização e o UAC localmente. A sessão atual será encerrada; depois de o cliente reconectar, solicite outra sessão.

O cliente elevado pode controlar aplicativos elevados compatíveis com o mesmo nível de integridade. A área segura do UAC e a tela de login não são transmitidas nem controladas. O recurso não altera o tipo de conta Windows, não concede privilégios por senha remota e não evita a confirmação UAC. Ao usar credenciais de outra conta Windows, essa conta terá seu próprio perfil e identidade. A inicialização automática na bandeja continua sem elevação automática.

## Validação

38 verificações de acesso e 19 regressões do servidor passaram. O teste WPF validou espera, campo de senha, caixa Salvar, limpeza, recusa e cancelamento. O teste de sessão confirmou barra apenas no destino, controles integrados no solicitante, recurso de cursor azul carregado, indicação do ponteiro não interativa e limpeza. O comando de reinício foi validado para usar runas e aguardar o encerramento da instância anterior.

A confirmação UAC real, o controle de um aplicativo elevado e a sessão completa entre dois PCs físicos permanecem pendentes. Não houve injeção de entrada nos testes desta entrega.

A barra flutuante tem 360 px no modo normal. O botão de traço minimiza para uma faixa de 180 px; o ícone de expansão restaura. O indicador e o botão Encerrar continuam disponíveis no modo minimizado. A seta mostra qualidade e detalhes da sessão.
