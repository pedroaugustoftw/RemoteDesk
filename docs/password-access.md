# Dispositivos e acesso por senha

Atualize primeiro o servidor com `RemoteDesk-Server-Setup.exe`, usando **Atualizar componentes**, e depois os dois clientes com `RemoteDesk-Setup.exe`. A atualização do servidor interrompe sessões; preserva configuração, certificado e registro de IDs. Mantém a pasta anterior de binários para recuperação. A instalação/atualização elevada ainda precisa ser validada no ambiente real.

No computador de destino, abra **Configurações → Acesso por senha**. Habilite a opção, cadastre e confirme uma senha de pelo menos 12 caracteres e selecione as permissões. Visualização é obrigatória; controle e arquivos são opcionais. Salve. O recurso vem desabilitado.

No computador de origem, abra **Dispositivos → Adicionar dispositivo**. Informe ID, apelido e acesso por senha. A caixa **Salvar senha** é opcional. Sem ela, a senha será solicitada ao conectar. O status online vem do servidor, atualizado periodicamente; sem conexão ao servidor o status é indisponível.

O destino precisa estar com o Windows conectado a uma conta e o cliente executando, inclusive minimizado na bandeja. Não funciona na tela de login nem contorna UAC. Toda sessão mostra o indicador permanente e permite encerramento imediato.

## Proteção

A política do destino armazena somente verificador PBKDF2-HMAC-SHA256 com sal aleatório e 600 mil iterações, protegido por DPAPI do usuário Windows. Senhas salvas na origem também usam DPAPI. Cinco falhas bloqueiam autenticações por cinco minutos.

A senha viaja em prova RSA-OAEP-SHA256 cifrada para a identidade do destino, vinculada à sessão e a desafio de uso único com validade de 30 segundos. O servidor encaminha a chave autenticada do destino. Após o primeiro sucesso, o dispositivo salvo fixa a impressão dessa chave e rejeita mudanças. A primeira conexão ainda depende da confiança no servidor e na sua configuração TLS.

O destino concede somente a interseção das permissões solicitadas com a política local. O relay continua recebendo conteúdo de tela/arquivos: a implementação atual não oferece criptografia ponta a ponta desses dados.

## Validação desta entrega

34 verificações de acesso passaram, incluindo política desabilitada, proteção DPAPI, prova Unicode, permissões, presença, senha incorreta, repetição e bloqueio. Também passaram 19 regressões do servidor e o teste de navegação/rolagem do cliente. Os testes usaram identidades no mesmo PC, sem injeção de entrada. A sessão por senha entre dois PCs físicos e a atualização elevada do serviço permanecem pendentes.
