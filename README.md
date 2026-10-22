# ESTATÍSTICAS 15ª BPM — instalador de teste

Este repositório contém o aplicativo Windows WPF e o instalador de teste.

## Regras já implementadas

- Aplicativo instalado em `C:\Program Files\Estatisticas15BPM`.
- Dados em `C:\Estatisticas15BPM\Dados` e backups em `C:\Estatisticas15BPM\Backup`.
- Banco por data: `estatisticas_15bpm_AAAA-MM-DD.db` (com sufixo numérico se forem criados mais de um no mesmo dia).
- Criação, seleção e importação de banco; exportação e backups `.bkp15`.
- Importação substitui o banco ativo somente após backup automático.
- O instalador preserva a pasta de dados na atualização ou desinstalação.
- O usuário técnico oculto `admin`/`admin` é criado em cada banco novo.

## Gerar o instalador

Execute em Windows, após instalar .NET 8 SDK e Inno Setup:

```powershell
dotnet publish .\Estatisticas15BPM\Estatisticas15BPM.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" .\installer\Estatisticas15BPM.iss
```

O artefato é criado em `outputs\Instalador_Estatisticas15BPM.exe`.
