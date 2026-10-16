# Ferramentas de investigação

Instalação validada em 2026-10-14.

- REA: `rea-agents@6.5.0`, registrado no Codex.
- Ghidra: `E:\AnyCraft\.tools\ghidra_12.1.4_PUBLIC`.
- Java: Temurin JDK 25.0.4.1, `E:\AnyCraft\.tools\java\jdk-25.0.4.1+1`.
- Configuração: `C:\Users\DELL\.codex\config.toml`, seção `mcp_servers.rea.env`.
- Backup anterior à configuração do Ghidra: `C:\Users\DELL\.codex\config.toml.ghidra.20261014133621.backup`.

Ambos os arquivos ZIP tiveram SHA-256 conferido contra os metadados das distribuições oficiais. Os metadados e arquivos estão em `.tools/downloads`.

O diagnóstico `rea doctor --client codex --provider ghidra` retornou `healthy: true`, incluindo validação do decompilador nativo x64, Java e controles de processos Windows. O analisador headless iniciou e mostrou sua ajuda (código 1 esperado por ausência de argumentos).

Os caminhos Java e Ghidra foram configurados apenas para o MCP REA; o Java do sistema não foi substituído. Reinicie o cliente Codex para carregar o novo servidor e seu ambiente. A validação local via CLI não comprova conexão MCP nesta sessão.

## Fontes oficiais

- https://github.com/NationalSecurityAgency/ghidra/releases/tag/Ghidra_12.1.4_build
- https://adoptium.net/
- https://github.com/morluto/rea

## Uso pelo terminal

```powershell
$env:GHIDRA_INSTALL_DIR = 'E:\AnyCraft\.tools\ghidra_12.1.4_PUBLIC'
$env:JAVA_HOME = 'E:\AnyCraft\.tools\java\jdk-25.0.4.1+1'
npx -y rea-agents@6.5.0 doctor --client codex --provider ghidra --json
```
