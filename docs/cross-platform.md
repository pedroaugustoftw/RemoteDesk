# RemoteDesk 1.1 — prévia multiplataforma

O cliente Windows WPF continua disponível como `RemoteDesk-Setup.exe`. O novo cliente em `portable/` usa Qt/Python e o mesmo protocolo RSA + WebSocket do servidor e relay existentes. Não usa servidores ou protocolo do aplicativo de referência.

## Baixar e configurar

- Windows 10/11 x64: instalador Windows ou ZIP portable. O ZIP Qt tem perfil próprio; não importa a identidade DPAPI do cliente WPF.
- Linux x64: pacote `.tar.gz`, construído em Ubuntu 22.04. Extraia e execute `./RemoteDesk`. Opcionalmente rode `./install-linux.sh` para adicionar ao menu do usuário. Precisa de ambiente gráfico, bibliotecas X11/Qt e cofre Secret Service desbloqueado (por exemplo GNOME Keyring). Não executar como root.
- macOS: escolha `.dmg` arm64 para Apple Silicon ou x64 para Intel. Arraste RemoteDesk para Applications. Build de prévia com assinatura ad-hoc, sem certificado Developer ID/notarização Apple. O sistema pode impedir a abertura; não há instalador assinado nesta versão.

No cliente Qt informe **Servidor**, **Relay** opcional e **SHA-256 TLS**. Todos devem usar a mesma infraestrutura:

| Ambiente | Servidor | Relay | SHA-256 |
|---|---|---|---|
| LAN | `wss://IP-DO-SERVIDOR:5081/ws` | vazio: usa anúncio do servidor | impressão exibida no servidor |
| Túnel HTTP existente | `wss://remote.example.com/ws` | `wss://remote.example.com/relay/ws` | vazio: valida certificado público pelo sistema |

O modo VPS do servidor precisa estar configurado com esses endereços públicos, gateway em `127.0.0.1:2750` e túnel apontando para `http://localhost:2750`. O hostname acima é exemplo da infraestrutura informada; nenhum certificado privado ou segredo é distribuído. A SHA não é senha de acesso nem chave privada.

Clique **Salvar e conectar ao servidor**. O ID é gerado no registro e permanece vinculado à chave do perfil e ao cadastro do servidor. Não apague o perfil nem o banco de dispositivos para atualizar. O segredo que protege a chave fica no cofre do sistema; se ele não estiver disponível, o cliente informa erro em vez de criar uma identidade nova silenciosamente.

Digite o ID remoto e clique Conectar. No destino Qt, cada sessão exige aceite visível; controle e arquivos são permissões separadas, inicialmente desmarcadas. Uma janela permanente de sessão ativa permite Encerrar. O cliente Qt pode solicitar acesso por senha a um destino Windows que já tenha essa opção habilitada, mas não oferece recebimento não supervisionado nem salvamento de senhas nesta prévia.

## Recursos e limites

Implementados no cliente Qt: registro RSA-PSS, TLS com validação pública ou pin SHA-256 exato, solicitação/aceite/recusa, captura JPEG contínua da tela principal, ajuste velocidade/qualidade, visualização, mouse/teclas compatíveis, janela de sessão ativa, dois exploradores, envio/recebimento em blocos, cancelamento e SHA-256. Arquivos existentes não são substituídos no cliente Qt; escolha outro nome. Sem pastas recursivas, áudio, múltiplos monitores ou equivalência visual completa com o cliente Windows.

**Linux:** receber tela/controle exige sessão X11. Wayland não recebe sessões nesta prévia, mas pode visualizar outros PCs e transferir arquivos. A distribuição Qt exige bibliotecas gráficas do sistema; não é um AppImage universal.

**macOS:** em Ajustes do Sistema → Privacidade e Segurança, conceda Gravação de Tela e Acessibilidade ao RemoteDesk para receber tela/controle; depois reabra. Sem essas permissões o sistema pode retornar tela vazia ou impedir a entrada. Não há bypass. O mapeamento de teclado cobre teclas usuais; layouts internacionais/IME e atalhos especiais precisam de validação prática.

**Windows:** o cliente portable opera no nível de privilégio do usuário. Nenhuma versão controla a área segura do UAC automaticamente. Use o cliente WPF instalado para os recursos Windows existentes.

Não há WebRTC/ICE/STUN/TURN nem criptografia ponta a ponta da imagem: o relay termina TLS. O operador do servidor é parte da confiança. Nenhuma porta externa de FTP é necessária.

## Compilar e testar

Python 3.14 e .NET 8. Em um ambiente virtual:

```text
python -m pip install -r portable/requirements.txt pyinstaller==6.22.3
dotnet build server/RemoteDesk.Server.csproj -c Release
dotnet build relay/RemoteDesk.Relay.csproj -c Release
```

Defina `PYTHONPATH=portable` no terminal e execute:

```text
python -m unittest discover -s portable/tests -v
python portable/tests/integration.py
python portable/launch.py
python scripts/build-portable.py
```

O empacotamento roda no próprio sistema de destino. A integração inicia broker e relay isolados em portas aleatórias, sem tocar no serviço instalado. CI roda testes e abertura do pacote nos quatro runners. Isso não substitui testes em dois computadores físicos nem concessão de permissões macOS. A versão é uma **prévia**, sem alegação de matriz física Windows ↔ Linux ↔ macOS já homologada. Acompanhe a evidência em Actions e valide uma sessão supervisionada antes de uso operacional.
