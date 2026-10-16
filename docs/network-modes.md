# Rede local e VPS/domínio

## Servidor Windows

1. Execute o novo RemoteDesk-Server-Setup.exe como administrador.
2. Em Configurações → Modo de conexão, selecione VPS / domínio / Cloudflare. Os campos LAN ficam ocultos.
3. Os controles do serviço ficam fixos no topo, fora da rolagem.
4. Servidor público: `wss://remote.example.com/ws`.
5. Relay público: `wss://remote.example.com/relay/ws`.
6. Porta HTTP local do túnel: `2750`.
7. Se ainda não estiver instalado, clique em Instalar e iniciar no topo. A instalação já aplica o modo preenchido. Se já estiver instalado, use Aplicar configuração para salvar somente a rede, ou Atualizar componentes para atualizar o programa e aplicar os campos preenchidos. As sessões atuais serão encerradas ao reiniciar.

O botão Iniciar aparece somente quando o serviço está parado; Parar aparece quando está em execução. O estado e a explicação permanecem visíveis nos dois modos. No modo LAN aparecem apenas IP, firewall e dados LAN; no modo VPS aparecem apenas os campos públicos e do túnel.

O serviço mantém as portas LAN e o certificado existentes. O gateway HTTP adicional escuta somente no próprio computador, em localhost. A API administrativa do relay não é publicada. O certificado e o cadastro de dispositivos são preservados. Ao voltar para Rede local, o gateway é desativado após reiniciar.

## Cloudflare Tunnel

Mantenha o hostname `remote.example.com` apontando para serviço HTTP `localhost:2750`. O túnel deve rodar no mesmo computador do servidor. O cliente atual não possui integração com login interativo do Cloudflare Access. Não configure desafios interativos para os caminhos WebSocket sem uma integração de autenticação compatível.

## Aplicativo cliente

Instale a nova versão e abra Configurações → Conexão. Selecione VPS / domínio / Cloudflare e informe `wss://remote.example.com/ws`. Deixe ambas as SHA vazias quando o domínio utilizar certificado público confiável. Clique em Salvar. O servidor informa automaticamente o relay público. O modo LAN mantém endereço e impressões locais ao alternar os modos.

Para LAN, selecione Rede local, informe `wss://IP-DO-SERVIDOR:5081/ws` e as SHA fornecidas pelo servidor.

## Verificação

Teste o segundo PC conectado à internet do celular. O ID deve aparecer e uma sessão autorizada deve transmitir imagem pelo mesmo domínio. O computador servidor e o túnel precisam permanecer ligados. A transmissão usa relay WebSocket; não há conexão P2P nem retomada automática de uma sessão encerrada. O TLS é terminado no Cloudflare; não há criptografia de mídia ponta a ponta.

A validação automatizada cobre preservação de identidade, alternância dos modos, rejeição de URLs inválidas e sessão com mídia passando por gateway HTTP local e upstream TLS fixado. Isso não substitui o teste real pela internet.
