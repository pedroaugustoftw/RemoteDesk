# RemoteDesk Relay

Componente ASP.NET Core independente para retransmitir frames, entrada autorizada e arquivos por WebSocket. O servidor registra sessões autorizadas por API privada e emite tickets separados de host/viewer; o relay aplica direção, permissões e expiração. Não implementa ICE/STUN/TURN ou conexão direta. TLS protege cada salto; operadores do relay conseguem acessar o conteúdo, pois ainda não há criptografia ponta a ponta.

## Windows LAN — implantação principal

Execute `scripts/Setup-Lan.ps1 -Ip <IPv4 LAN>` uma vez, depois `scripts/Start-Lan.ps1`. O relay escuta WSS em `5091`; a API privada está ligada somente ao loopback HTTP `5092`. Um segredo aleatório compartilhado com o servidor é protegido por DPAPI do usuário e passado apenas ao ambiente dos processos. Não há serviço Windows, mudança de firewall ou importação automática de certificado. Confira `docs/install.md`.

## Configuração

- `RemoteDesk:RelaySecret`: pelo menos 32 caracteres; usado pela API privada de registro/revogação/renovação.
- `RemoteDesk:KnownProxies`: IPs explícitos do proxy TLS, apenas para implantação com proxy.
- `/ws`: WebSocket público TLS; ticket é enviado na mensagem de attach, sem credenciais na URL.
- `POST /sessions`, `PUT /sessions/{id}`, `DELETE /sessions/{id}`: API privada, com Bearer secret. Nunca expor essas rotas pelo proxy público.
- `/health`: verifica inicialização do processo; não comprova uma sessão remota funcionando.

## VPS opcional — não necessária para LAN

O Dockerfile deve ser compilado a partir da raiz RemoteDesk: `docker build -f relay/Dockerfile -t remotedesk-relay .`. O Compose usa publicações framework-dependent copiadas para `server-publish/` e `relay-publish/`. Configure `REMOTEDESK_HOST`, `REMOTEDESK_RELAY_SECRET`, domínio no Nginx e certificados TLS. O Nginx público encaminha `/relay/ws` para `/ws` no relay e bloqueia `/relay/sessions`. Sua porta interna `8082`, sem publicação externa, permite API apenas do IP fixo do servidor.

O transporte HTTP interno do Compose permanece na rede do mesmo host; não o mova para outro computador sem TLS autenticado. A sub-rede e IPs estáticos precisam ser alterados juntos se houver conflito. Este modelo VPS não foi testado em uma VPS externa; a entrega principal usa Windows LAN.
