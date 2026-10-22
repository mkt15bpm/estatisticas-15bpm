using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.IO;
using System.Linq;

namespace Estatisticas15BPM;

public sealed class DatabaseService
{
    public const string RootDirectory = @"C:\Estatisticas15BPM";
    public const string DataDirectory = RootDirectory + @"\Dados";
    public const string BackupDirectory = RootDirectory + @"\Backup";
    private const string ActiveFile = RootDirectory + @"\BancoAtivo.txt";
    private readonly SemaphoreSlim _automaticBackupGate=new(1,1);
    private int _backupRevision;
    private int _backupRevisionConcluida;
    private int _backupWorkerRunning;
    private string? _dashboardNatureza;
    public void SetDashboardNaturezaFilter(string? natureza) => _dashboardNatureza=natureza;
    public DatabaseService() { Directory.CreateDirectory(DataDirectory); Directory.CreateDirectory(BackupDirectory); if(GetActiveDatabase() is string active) { Initialize(active); ApplyCleanupMigration(active); ApplyApprovedStaffUpdate(active); } }
    public List<string> FindDatabases() => Directory.EnumerateFiles(DataDirectory, "estatisticas_15bpm_*.db").OrderByDescending(File.GetCreationTime).ToList();
    public string? GetActiveDatabase()
    {
        if (!File.Exists(ActiveFile)) return null;
        var database = File.ReadAllText(ActiveFile).Trim();
        return IsDatabaseInDataDirectory(database) && File.Exists(database) ? database : null;
    }
    public void Activate(string database)
    {
        var fullPath = Path.GetFullPath(database);
        if (!IsDatabaseInDataDirectory(fullPath)) throw new InvalidOperationException("O banco selecionado não pertence à pasta de dados do sistema.");
        Initialize(fullPath);
        ApplyCleanupMigration(fullPath);
        WriteActiveDatabase(fullPath);
    }
    public void CreateAndActivate() => Activate(CreateDatabasePath());
    public void Export(string destination) => CreateConsistentBackup(RequireActive(), destination);
    public string CreateAutomaticBackup(IProgress<int>? progress=null)
    {
        _automaticBackupGate.Wait();
        try { return CreateAutomaticBackupCore(progress); }
        finally { _automaticBackupGate.Release(); }
    }
    private string CreateAutomaticBackupCore(IProgress<int>? progress=null)
    {
        var source=RequireActive();
        var target=Path.Combine(BackupDirectory,$"bkp-{DateTime.Today:dd-MM-yyyy}.bkp15");
        progress?.Report(15);
        var backupAtualizado=File.Exists(target) && File.GetLastWriteTimeUtc(target)>=GetLatestDatabaseWriteUtc(source);
        if(!backupAtualizado)
        {
            progress?.Report(35);
            CreateConsistentBackup(source,target);
            progress?.Report(85);
        }
        else progress?.Report(85);
        foreach(var old in Directory.EnumerateFiles(BackupDirectory,"bkp-*.bkp15").OrderByDescending(File.GetLastWriteTime).Skip(10))File.Delete(old);
        progress?.Report(100);
        return target;
    }
    private void ScheduleAutomaticBackup()
    {
        Interlocked.Increment(ref _backupRevision);
        if(Interlocked.Exchange(ref _backupWorkerRunning,1)==1)return;
        _=Task.Run(async ()=>
        {
            try
            {
                await Task.Delay(700);
                while(Volatile.Read(ref _backupRevisionConcluida)<Volatile.Read(ref _backupRevision))
                {
                    var revision=Volatile.Read(ref _backupRevision);
                    await _automaticBackupGate.WaitAsync();
                    try { CreateAutomaticBackupCore(); }
                    catch { break; }
                    finally { _automaticBackupGate.Release(); }
                    Volatile.Write(ref _backupRevisionConcluida,revision);
                }
            }
            finally { Interlocked.Exchange(ref _backupWorkerRunning,0); }
        });
    }
    private static DateTime GetLatestDatabaseWriteUtc(string database)
    {
        var latest=File.GetLastWriteTimeUtc(database);
        foreach(var suffix in new[]{"-wal","-journal"})
        {
            var auxiliary=database+suffix;
            if(File.Exists(auxiliary) && File.GetLastWriteTimeUtc(auxiliary)>latest) latest=File.GetLastWriteTimeUtc(auxiliary);
        }
        return latest;
    }
    public string? GetLatestAutomaticBackup()=>Directory.Exists(BackupDirectory)?Directory.EnumerateFiles(BackupDirectory,"bkp-*.bkp15").OrderByDescending(File.GetLastWriteTime).FirstOrDefault():null;
    public StorageResumo GetStorageResumo(){var drive=new DriveInfo(Path.GetPathRoot(DataDirectory)!);return new StorageResumo(drive.TotalSize-drive.AvailableFreeSpace,drive.AvailableFreeSpace,drive.TotalSize);}
    public string CreateDeletionBackup(string database)
    {
        var fullPath=Path.GetFullPath(database);var dataPath=Path.GetFullPath(DataDirectory)+Path.DirectorySeparatorChar;
        if(!fullPath.StartsWith(dataPath,StringComparison.OrdinalIgnoreCase)||!File.Exists(fullPath))throw new InvalidOperationException("Banco de dados não encontrado.");
        if(string.Equals(fullPath,GetActiveDatabase(),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("O banco que está em uso não pode ser excluído. Selecione outro banco antes de continuar.");
        var backup=Path.Combine(BackupDirectory,$"Backup_15BPM_antes_exclusao_{Path.GetFileNameWithoutExtension(fullPath)}_{DateTime.Now:yyyy-MM-dd_HHmmss}.bkp15");
        CreateConsistentBackup(fullPath,backup);return backup;
    }
    public void DeleteDatabasePermanently(string database){var fullPath=Path.GetFullPath(database);if(string.Equals(fullPath,GetActiveDatabase(),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("O banco que está em uso não pode ser excluído. Selecione outro banco antes de continuar.");if(!File.Exists(fullPath))throw new InvalidOperationException("Banco de dados não encontrado.");File.Delete(fullPath);foreach(var suffix in new[]{"-wal","-shm"}){var aux=fullPath+suffix;if(File.Exists(aux))File.Delete(aux);}}
    public void ImportAndActivate(string source)
    {
        var sourcePath = Path.GetFullPath(source);
        ValidateImport(sourcePath);
        var active = GetActiveDatabase();
        var destination = CreateDatabasePath();
        try
        {
            CreateConsistentBackup(sourcePath, destination);
            Initialize(destination);
            if (active is not null)
                CreateConsistentBackup(active, Path.Combine(BackupDirectory, $"Backup_15BPM_antes_importacao_{DateTime.Now:yyyy-MM-dd_HHmmss}.bkp15"));
            WriteActiveDatabase(destination);
        }
        catch
        {
            DeleteDatabaseFiles(destination);
            throw;
        }
    }
    public List<DashboardQuartelResumo> GetDashboardQuartel(DateTime inicio,DateTime fim,string tipo,string? quartel=null,string? natureza=null){using var db=Connect();using var c=db.CreateCommand();var other=tipo=="OUTRAS OCORRÊNCIAS GERAIS";var alias=other?"o":"t";var tabela=other?"OutrasOcorrencias o":"Tcos t";c.CommandText=$"SELECT {alias}.Quartel,COUNT(*) FROM {tabela} WHERE {alias}.Status='ATIVO' AND {alias}.DataFato BETWEEN $i AND $f"+(string.IsNullOrWhiteSpace(quartel)?"":$" AND {alias}.Quartel=$q")+(string.IsNullOrWhiteSpace(natureza)?"":other?" AND o.Natureza=$n":" AND EXISTS(SELECT 1 FROM TcoNaturezas filtroTco JOIN Naturezas filtroNatureza ON filtroNatureza.Id=filtroTco.NaturezaId WHERE filtroTco.TcoId=t.Id AND filtroNatureza.Descricao=$n)")+$" GROUP BY {alias}.Quartel ORDER BY 2 DESC,1";c.Parameters.AddWithValue("$i",inicio.ToString("yyyy-MM-dd"));c.Parameters.AddWithValue("$f",fim.ToString("yyyy-MM-dd"));if(!string.IsNullOrWhiteSpace(quartel))c.Parameters.AddWithValue("$q",quartel);if(!string.IsNullOrWhiteSpace(natureza))c.Parameters.AddWithValue("$n",natureza);using var r=c.ExecuteReader();var result=new List<DashboardQuartelResumo>();while(r.Read())result.Add(new DashboardQuartelResumo(r.GetString(0),r.GetInt64(1)));return result;}
    public List<ContagemResumo> GetDashboardNaturezas(DateTime inicio,DateTime fim,string tipo,string? quartel=null,string? natureza=null)
    {
        using var db=Connect();using var c=db.CreateCommand();var isOther=tipo=="OUTRAS OCORRÊNCIAS GERAIS";
        c.CommandText=isOther?"SELECT o.Natureza,COUNT(*) FROM OutrasOcorrencias o WHERE o.Status='ATIVO' AND o.DataFato BETWEEN $i AND $f":"SELECT n.Descricao,COUNT(*) FROM Tcos t JOIN TcoNaturezas tn ON tn.TcoId=t.Id JOIN Naturezas n ON n.Id=tn.NaturezaId WHERE t.Status='ATIVO' AND t.DataFato BETWEEN $i AND $f";
        if(!string.IsNullOrWhiteSpace(quartel))c.CommandText+=" AND "+(isOther?"o":"t")+".Quartel=$q";if(!string.IsNullOrWhiteSpace(natureza))c.CommandText+=" AND "+(isOther?"o.Natureza=$n":"n.Descricao=$n");c.CommandText+=" GROUP BY 1 ORDER BY 2 DESC,1";c.Parameters.AddWithValue("$i",inicio.ToString("yyyy-MM-dd"));c.Parameters.AddWithValue("$f",fim.ToString("yyyy-MM-dd"));if(!string.IsNullOrWhiteSpace(quartel))c.Parameters.AddWithValue("$q",quartel);if(!string.IsNullOrWhiteSpace(natureza))c.Parameters.AddWithValue("$n",natureza);using var r=c.ExecuteReader();var result=new List<ContagemResumo>();while(r.Read())result.Add(new ContagemResumo(r.GetString(0),r.GetInt64(1)));return result;
    }
    public List<ContagemResumo> GetDashboardSerie(DateTime referencia,string escala,string tipo,string? quartel=null,string? natureza=null)
    {
        var start=escala=="ANUAL"?new DateTime(referencia.Year-4,1,1):escala=="MENSAL"?new DateTime(referencia.Year,1,1):new DateTime(referencia.Year,referencia.Month,1);var end=escala=="ANUAL"?new DateTime(referencia.Year,12,31):escala=="MENSAL"?new DateTime(referencia.Year,12,31):start.AddMonths(1).AddDays(-1);var labels=escala=="ANUAL"?Enumerable.Range(referencia.Year-4,5).Select(x=>x.ToString()).ToArray():escala=="MENSAL"?new[]{"JAN","FEV","MAR","ABR","MAI","JUN","JUL","AGO","SET","OUT","NOV","DEZ"}:Enumerable.Range(1,DateTime.DaysInMonth(referencia.Year,referencia.Month)).Select(x=>x.ToString("00")).ToArray();using var db=Connect();using var c=db.CreateCommand();var other=tipo=="OUTRAS OCORRÊNCIAS GERAIS";var alias=other?"o":"t";var tabela=other?"OutrasOcorrencias o":"Tcos t";var slice=escala=="ANUAL"?$"substr({alias}.DataFato,1,4)":escala=="MENSAL"?$"substr({alias}.DataFato,6,2)":$"substr({alias}.DataFato,9,2)";c.CommandText=$"SELECT {slice},COUNT(*) FROM {tabela} WHERE {alias}.Status='ATIVO' AND {alias}.DataFato BETWEEN $i AND $f"+(string.IsNullOrWhiteSpace(quartel)?"":$" AND {alias}.Quartel=$q")+(string.IsNullOrWhiteSpace(natureza)?"":other?" AND o.Natureza=$n":" AND EXISTS(SELECT 1 FROM TcoNaturezas filtroTco JOIN Naturezas filtroNatureza ON filtroNatureza.Id=filtroTco.NaturezaId WHERE filtroTco.TcoId=t.Id AND filtroNatureza.Descricao=$n)")+$" GROUP BY {slice}";c.Parameters.AddWithValue("$i",start.ToString("yyyy-MM-dd"));c.Parameters.AddWithValue("$f",end.ToString("yyyy-MM-dd"));if(!string.IsNullOrWhiteSpace(quartel))c.Parameters.AddWithValue("$q",quartel);if(!string.IsNullOrWhiteSpace(natureza))c.Parameters.AddWithValue("$n",natureza);var counts=new Dictionary<string,long>();using var r=c.ExecuteReader();while(r.Read())counts[r.GetString(0)]=r.GetInt64(1);return labels.Select((label,index)=>new ContagemResumo(label,counts.GetValueOrDefault(escala=="MENSAL"?(index+1).ToString("00"):label))).ToList();
    }
    public List<DashboardRegistroResumo> GetDashboardRegistros(DateTime inicio,DateTime fim,string tipo,string? quartel=null,int limite=500,string? natureza=null){using var db=Connect();using var c=db.CreateCommand();var tco=tipo!="OUTRAS OCORRÊNCIAS GERAIS";var sql=tco?"SELECT t.Id,'TCO',t.Numero,t.DataFato,t.HoraFato,t.Quartel,IFNULL((SELECT group_concat(n.Descricao,'; ') FROM TcoNaturezas tn JOIN Naturezas n ON n.Id=tn.NaturezaId WHERE tn.TcoId=t.Id),''),IFNULL((SELECT group_concat(u.PostoGraduacao || ' PM ' || u.Nome,'; ') FROM TcoGuarnicao g JOIN Usuarios u ON u.Id=g.UsuarioId WHERE g.TcoId=t.Id),'') FROM Tcos t WHERE t.Status='ATIVO' AND t.DataFato BETWEEN $i AND $f":"SELECT o.Id,'OUTRA OCORRÊNCIA',o.Numero,o.DataFato,o.HoraFato,o.Quartel,o.Natureza,IFNULL((SELECT group_concat(u.PostoGraduacao || ' PM ' || u.Nome,'; ') FROM OutrasGuarnicao g JOIN Usuarios u ON u.Id=g.UsuarioId WHERE g.OcorrenciaId=o.Id),'') FROM OutrasOcorrencias o WHERE o.Status='ATIVO' AND o.DataFato BETWEEN $i AND $f";if(!string.IsNullOrWhiteSpace(quartel))sql+=" AND "+(tco?"t":"o")+".Quartel=$q";if(!string.IsNullOrWhiteSpace(natureza))sql+=tco?" AND EXISTS(SELECT 1 FROM TcoNaturezas filtroTco JOIN Naturezas filtroNatureza ON filtroNatureza.Id=filtroTco.NaturezaId WHERE filtroTco.TcoId=t.Id AND filtroNatureza.Descricao=$n)":" AND o.Natureza=$n";sql+=" ORDER BY 4 DESC,5 DESC LIMIT $l";c.CommandText=sql;c.Parameters.AddWithValue("$i",inicio.ToString("yyyy-MM-dd"));c.Parameters.AddWithValue("$f",fim.ToString("yyyy-MM-dd"));if(!string.IsNullOrWhiteSpace(quartel))c.Parameters.AddWithValue("$q",quartel);if(!string.IsNullOrWhiteSpace(natureza))c.Parameters.AddWithValue("$n",natureza);c.Parameters.AddWithValue("$l",limite);using var r=c.ExecuteReader();var result=new List<DashboardRegistroResumo>();while(r.Read())result.Add(new DashboardRegistroResumo(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7)));return result;}
    public List<DashboardQuartelResumo> GetDashboardQuartel(DateTime inicio,DateTime fim,string tipo,string? quartel) => GetDashboardQuartel(inicio,fim,tipo,quartel,_dashboardNatureza);
    public List<ContagemResumo> GetDashboardNaturezas(DateTime inicio,DateTime fim,string tipo,string? quartel) => GetDashboardNaturezas(inicio,fim,tipo,quartel,_dashboardNatureza);
    public List<ContagemResumo> GetDashboardSerie(DateTime referencia,string escala,string tipo,string? quartel) => GetDashboardSerie(referencia,escala,tipo,quartel,_dashboardNatureza);
    public List<DashboardRegistroResumo> GetDashboardRegistros(DateTime inicio,DateTime fim,string tipo,string? quartel,int limite) => GetDashboardRegistros(inicio,fim,tipo,quartel,limite,_dashboardNatureza);
    public UserSession? Authenticate(string identifier, string password)
    {
        using var db = Connect(); using var c = db.CreateCommand(); c.CommandText = "SELECT Id,Login,Nome,Funcao,SenhaHash,Oculto,SenhaInicial FROM Usuarios WHERE (Login=$id OR Documento=$id) AND Situacao='ATIVO' LIMIT 1"; c.Parameters.AddWithValue("$id", identifier.Trim()); using var r = c.ExecuteReader();
        return r.Read() && Verify(password, r.GetString(4)) ? new UserSession(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(5) == 1,r.GetInt64(6)==1) : null;
    }
    public SqliteConnection Connect()
    {
        var db = new SqliteConnection($"Data Source={RequireActive()}");
        db.Open();
        ConfigureConnection(db);
        return db;
    }
    public void Log(UserSession u, string registro, string descricao) { using var db = Connect(); using var c = db.CreateCommand(); c.CommandText = "INSERT INTO Logs(DataHora,Usuario,Funcao,Registro,Descricao,Permanente) VALUES($d,$u,$f,$r,$x,0)"; c.Parameters.AddWithValue("$d", DateTime.Now.ToString("O")); c.Parameters.AddWithValue("$u",u.Login); c.Parameters.AddWithValue("$f",u.Role); c.Parameters.AddWithValue("$r",registro); c.Parameters.AddWithValue("$x",descricao); c.ExecuteNonQuery(); if(descricao.Contains("cadastrad",StringComparison.OrdinalIgnoreCase))ScheduleAutomaticBackup(); }
    public List<string> GetQuarteis() { using var db=Connect(); using var c=db.CreateCommand(); c.CommandText="SELECT Nome FROM Quarteis WHERE Situacao='ATIVO' ORDER BY Nome"; using var r=c.ExecuteReader(); var x=new List<string>(); while(r.Read())x.Add(r.GetString(0)); return x; }
    public List<Natureza> GetNaturezas(string modulo) { using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT Id,Artigo,Descricao FROM Naturezas WHERE Modulo=$m AND Situacao='ATIVO' AND Aprovada=1 ORDER BY Artigo,Descricao";c.Parameters.AddWithValue("$m",modulo);using var r=c.ExecuteReader();var x=new List<Natureza>();while(r.Read())x.Add(new Natureza(r.GetInt64(0),r.IsDBNull(1)?"":r.GetString(1),r.GetString(2)));return x; }
    public List<Policial> GetPoliciais(string? quartel=null) { using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT Id,PostoGraduacao,Nome,Login,Quartel FROM Usuarios WHERE Oculto=0 AND Situacao='ATIVO' AND length(replace(replace(replace(replace(IFNULL(Documento,''),'.',''),'-',''),'(',''),')',''))<>11"+(quartel is null?"":" AND Quartel=$q")+" ORDER BY CASE PostoGraduacao WHEN 'CORONEL' THEN 1 WHEN 'TEN. CORONEL' THEN 2 WHEN 'MAJOR' THEN 3 WHEN 'CAPITÃO' THEN 4 WHEN '1º SARGENTO' THEN 8 WHEN '2º SARGENTO' THEN 9 WHEN '3º SARGENTO' THEN 10 WHEN 'CABO' THEN 11 WHEN 'SOLDADO' THEN 12 ELSE 99 END, Nome";if(quartel is not null)c.Parameters.AddWithValue("$q",quartel);using var r=c.ExecuteReader();var x=new List<Policial>();while(r.Read())x.Add(new Policial(r.GetInt64(0),r.IsDBNull(1)?"":r.GetString(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?"":r.GetString(4)));return x; }
    public List<UsuarioResumo> GetUsuarios(){using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT Id,Nome,PostoGraduacao,Login,Quartel,Funcao,Situacao FROM Usuarios WHERE Oculto=0 AND Situacao<>'EXCLUIDO' AND (Funcao='ADMINISTRADOR' OR length(replace(replace(replace(replace(IFNULL(Documento,''),'.',''),'-',''),'(',''),')',''))<>11) ORDER BY Nome";using var r=c.ExecuteReader();var x=new List<UsuarioResumo>();while(r.Read())x.Add(new UsuarioResumo(r.GetInt64(0),r.GetString(1),r.IsDBNull(2)?"":r.GetString(2),r.GetString(3),r.IsDBNull(4)?"":r.GetString(4),r.GetString(5),r.GetString(6)));return x;}
    public void CreateUsuario(string login,string nome,string posto,string quartel,string funcao,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="INSERT INTO Usuarios(Login,Documento,Nome,PostoGraduacao,Quartel,Funcao,Situacao,SenhaHash,Oculto,SenhaInicial) VALUES($l,$l,$n,$p,$q,$f,'ATIVO',$s,0,1)";c.Parameters.AddWithValue("$l",login.Trim());c.Parameters.AddWithValue("$n",nome.Trim().ToUpper());c.Parameters.AddWithValue("$p",posto);c.Parameters.AddWithValue("$q",quartel);c.Parameters.AddWithValue("$f",funcao);c.Parameters.AddWithValue("$s",Hash("15bpm"));c.ExecuteNonQuery();Log(actor,"USUÁRIO",$"Usuário {login} cadastrado.");}
    public void UpdateUsuario(UsuarioResumo old,string login,string nome,string posto,string quartel,string funcao,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Usuarios SET Login=$l,Documento=$l,Nome=$n,PostoGraduacao=$p,Quartel=$q,Funcao=$f WHERE Id=$id";c.Parameters.AddWithValue("$l",login.Trim());c.Parameters.AddWithValue("$n",nome.Trim().ToUpper());c.Parameters.AddWithValue("$p",posto);c.Parameters.AddWithValue("$q",quartel);c.Parameters.AddWithValue("$f",funcao);c.Parameters.AddWithValue("$id",old.Id);c.ExecuteNonQuery();Log(actor,"USUÁRIO",$"Usuário {old.Login} alterado.");}
    public void InactivateUsuario(long id,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Usuarios SET Situacao='INATIVO' WHERE Id=$id AND Oculto=0";c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();Log(actor,"USUÁRIO",$"Usuário #{id} inativado.");}
    public void ExcludeUsuario(long id,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Usuarios SET Situacao='EXCLUIDO' WHERE Id=$id AND Oculto=0";c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();Log(actor,"USUÁRIO",$"Usuário #{id} excluído logicamente.");}
    public void ChangePassword(UserSession user,string password){if(password.Length<6)throw new InvalidOperationException("A nova senha deve ter pelo menos 6 caracteres.");using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Usuarios SET SenhaHash=$s,SenhaInicial=0 WHERE Id=$id";c.Parameters.AddWithValue("$s",Hash(password));c.Parameters.AddWithValue("$id",user.Id);c.ExecuteNonQuery();Log(user,"USUÁRIO","Senha alterada.");}
    public void ResetUserPassword(long userId,UserSession actor){if(actor.Role!="ADMINISTRADOR")throw new InvalidOperationException("Somente administradores podem redefinir senhas.");using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Usuarios SET SenhaHash=$s,SenhaInicial=1 WHERE Id=$id AND Oculto=0";c.Parameters.AddWithValue("$s",Hash("15bpm"));c.Parameters.AddWithValue("$id",userId);if(c.ExecuteNonQuery()==0)throw new InvalidOperationException("Usuário não encontrado ou não disponível para redefinição.");Log(actor,"USUÁRIO",$"Senha do usuário #{userId} redefinida pelo administrador.");}
    public List<NaturezaResumo> GetAllNaturezas(){using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT Id,Modulo,IFNULL(Artigo,''),Descricao,Situacao FROM Naturezas WHERE Situacao<>'EXCLUIDO' ORDER BY Modulo,Artigo,Descricao";using var r=c.ExecuteReader();var x=new List<NaturezaResumo>();while(r.Read())x.Add(new NaturezaResumo(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4)));return x;}
    public void CreateNatureza(string modulo,string artigo,string descricao,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="INSERT INTO Naturezas(Modulo,Artigo,Descricao,Aprovada) VALUES($m,$a,$d,1)";c.Parameters.AddWithValue("$m",modulo);c.Parameters.AddWithValue("$a",artigo.Trim());c.Parameters.AddWithValue("$d",descricao.Trim().ToUpper());c.ExecuteNonQuery();Log(actor,"NATUREZA",$"Natureza {descricao} cadastrada.");}
    public void CreateQuartel(string nome,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="INSERT INTO Quarteis(Nome) VALUES($n)";c.Parameters.AddWithValue("$n",nome.Trim().ToUpper());c.ExecuteNonQuery();Log(actor,"QUARTEL",$"Quartel {nome} cadastrado.");}
    public List<QuartelResumo> GetAllQuarteis(){using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT Id,Nome,Situacao FROM Quarteis ORDER BY Nome";using var r=c.ExecuteReader();var x=new List<QuartelResumo>();while(r.Read())x.Add(new QuartelResumo(r.GetInt64(0),r.GetString(1),r.GetString(2)));return x;}
    public void UpdateQuartel(QuartelResumo old,string nome,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Quarteis SET Nome=$n WHERE Id=$id";c.Parameters.AddWithValue("$n",nome.Trim().ToUpper());c.Parameters.AddWithValue("$id",old.Id);c.ExecuteNonQuery();Log(actor,"QUARTEL",$"Quartel {old.Nome} alterado.");}
    public void InactivateQuartel(long id,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Quarteis SET Situacao='INATIVO' WHERE Id=$id";c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();Log(actor,"QUARTEL",$"Quartel #{id} inativado.");}
    public List<UsuarioResumo> GetUsuariosDoQuartel(string quartel){using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT Id,Nome,PostoGraduacao,Login,Quartel,Funcao,Situacao FROM Usuarios WHERE Oculto=0 AND Situacao<>'EXCLUIDO' AND length(replace(replace(replace(replace(IFNULL(Documento,''),'.',''),'-',''),'(',''),')',''))<>11 AND Quartel=$q ORDER BY Nome";c.Parameters.AddWithValue("$q",quartel);using var r=c.ExecuteReader();var x=new List<UsuarioResumo>();while(r.Read())x.Add(new UsuarioResumo(r.GetInt64(0),r.GetString(1),r.IsDBNull(2)?"":r.GetString(2),r.GetString(3),r.IsDBNull(4)?"":r.GetString(4),r.GetString(5),r.GetString(6)));return x;}
    public void DeleteQuartel(long id,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT Nome FROM Quarteis WHERE Id=$id";c.Parameters.AddWithValue("$id",id);var nome=c.ExecuteScalar() as string;if(nome is null)throw new InvalidOperationException("Quartel não encontrado.");using var count=db.CreateCommand();count.CommandText="SELECT COUNT(*) FROM Usuarios WHERE Quartel=$q AND Situacao='ATIVO'";count.Parameters.AddWithValue("$q",nome);if((long)count.ExecuteScalar()!>0)throw new InvalidOperationException("Transfira ou inative os operadores ativos antes de excluir este quartel.");using var del=db.CreateCommand();del.CommandText="DELETE FROM Quarteis WHERE Id=$id";del.Parameters.AddWithValue("$id",id);del.ExecuteNonQuery();Log(actor,"QUARTEL",$"Quartel {nome} excluído.");}
    public void UpdateNatureza(NaturezaResumo old,string modulo,string artigo,string descricao,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Naturezas SET Modulo=$m,Artigo=$a,Descricao=$d WHERE Id=$id";c.Parameters.AddWithValue("$m",modulo);c.Parameters.AddWithValue("$a",artigo.Trim());c.Parameters.AddWithValue("$d",descricao.Trim().ToUpper());c.Parameters.AddWithValue("$id",old.Id);c.ExecuteNonQuery();Log(actor,"NATUREZA",$"Natureza {old.Descricao} alterada.");}
    public void InactivateNatureza(long id,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Naturezas SET Situacao='INATIVO' WHERE Id=$id";c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();Log(actor,"NATUREZA",$"Natureza #{id} inativada.");}
    public void ExcludeNatureza(long id,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Naturezas SET Situacao='EXCLUIDO' WHERE Id=$id";c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();Log(actor,"NATUREZA",$"Natureza #{id} excluída logicamente.");}
    public List<LogResumo> GetLogs(){using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT DataHora,Usuario,Registro,Descricao FROM Logs ORDER BY Id DESC LIMIT 100";using var r=c.ExecuteReader();var x=new List<LogResumo>();while(r.Read())x.Add(new LogResumo(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3)));return x;}
    public void ClearLogs(UserSession actor){if(actor.Login!="admin")throw new InvalidOperationException("Somente o usuário técnico admin pode apagar todos os logs.");using var db=Connect();using var c=db.CreateCommand();c.CommandText="DELETE FROM Logs";c.ExecuteNonQuery();}
    public EstatisticasResumo GetEstatisticas(DateTime inicio,DateTime fim,string? quartel,string tipo="TCO",string? natureza=null)
    {
        using var db=Connect(); var i=inicio.ToString("yyyy-MM-dd"); var f=fim.ToString("yyyy-MM-dd");
        bool tco=tipo!="OUTRAS OCORRÊNCIAS GERAIS", other=tipo!="TCO";
        string Where(string alias)=>$"{alias}.Status='ATIVO' AND {alias}.DataFato BETWEEN $i AND $f"+(string.IsNullOrWhiteSpace(quartel)?"":$" AND {alias}.Quartel=$q");
        string ParticipationWhere(string alias)=>$"{alias}.Status='ATIVO' AND {alias}.DataFato BETWEEN $i AND $f";
        long Count(string table,string alias,string extra="") { using var c=db.CreateCommand(); c.CommandText=$"SELECT COUNT(*) FROM {table} {alias} WHERE {Where(alias)} {extra}"; c.Parameters.AddWithValue("$i",i); c.Parameters.AddWithValue("$f",f); if(!string.IsNullOrWhiteSpace(quartel)) c.Parameters.AddWithValue("$q",quartel); if(!string.IsNullOrWhiteSpace(natureza)) c.Parameters.AddWithValue("$n",natureza); return (long)c.ExecuteScalar()!; }
        var tcoCount=tco?Count("Tcos","t",string.IsNullOrWhiteSpace(natureza)?"":"AND EXISTS(SELECT 1 FROM TcoNaturezas x JOIN Naturezas n ON n.Id=x.NaturezaId WHERE x.TcoId=t.Id AND n.Descricao=$n)"):0;
        var otherCount=other?Count("OutrasOcorrencias","o",string.IsNullOrWhiteSpace(natureza)?"":"AND o.Natureza=$n"):0;
        var naturezas=new List<ContagemResumo>(); var parts=new List<string>();
        if(tco) parts.Add($"SELECT n.Descricao,COUNT(*) FROM TcoNaturezas tn JOIN Tcos t ON t.Id=tn.TcoId JOIN Naturezas n ON n.Id=tn.NaturezaId WHERE {Where("t")}"+(string.IsNullOrWhiteSpace(natureza)?"":" AND n.Descricao=$n")+" GROUP BY n.Descricao");
        if(other) parts.Add($"SELECT o.Natureza,COUNT(*) FROM OutrasOcorrencias o WHERE {Where("o")}"+(string.IsNullOrWhiteSpace(natureza)?"":" AND o.Natureza=$n")+" GROUP BY o.Natureza");
        using(var c=db.CreateCommand()) { c.CommandText=string.Join(" UNION ALL ",parts)+" ORDER BY 2 DESC"; c.Parameters.AddWithValue("$i",i); c.Parameters.AddWithValue("$f",f); if(!string.IsNullOrWhiteSpace(quartel)) c.Parameters.AddWithValue("$q",quartel); if(!string.IsNullOrWhiteSpace(natureza)) c.Parameters.AddWithValue("$n",natureza); using var r=c.ExecuteReader(); while(r.Read()) naturezas.Add(new ContagemResumo(r.GetString(0),r.GetInt64(1))); }
        var participation=new List<string>();
        if(tco) participation.Add($"SELECT g.UsuarioId FROM TcoGuarnicao g JOIN Tcos t ON t.Id=g.TcoId WHERE {ParticipationWhere("t")}"+(string.IsNullOrWhiteSpace(natureza)?"":" AND EXISTS(SELECT 1 FROM TcoNaturezas x JOIN Naturezas n ON n.Id=x.NaturezaId WHERE x.TcoId=t.Id AND n.Descricao=$n)"));
        if(other) participation.Add($"SELECT g.UsuarioId FROM OutrasGuarnicao g JOIN OutrasOcorrencias o ON o.Id=g.OcorrenciaId WHERE {ParticipationWhere("o")}"+(string.IsNullOrWhiteSpace(natureza)?"":" AND o.Natureza=$n"));
        var ranking=new List<ContagemResumo>();
        var hierarchy="CASE u.PostoGraduacao WHEN 'CORONEL' THEN 1 WHEN 'TEN. CORONEL' THEN 2 WHEN 'MAJOR' THEN 3 WHEN 'CAPITÃO' THEN 4 WHEN 'SUBTENENTE' THEN 7 WHEN '1º SARGENTO' THEN 8 WHEN '2º SARGENTO' THEN 9 WHEN '3º SARGENTO' THEN 10 WHEN 'CABO' THEN 11 WHEN 'SOLDADO' THEN 12 ELSE 99 END";
        using(var c=db.CreateCommand())
        {
            c.CommandText=$"SELECT u.PostoGraduacao || ' PM ' || u.Nome,COUNT(p.UsuarioId) FROM Usuarios u LEFT JOIN ({string.Join(" UNION ALL ",participation)}) p ON p.UsuarioId=u.Id WHERE u.Oculto=0 AND u.Situacao='ATIVO' AND length(replace(replace(replace(replace(IFNULL(u.Documento,''),'.',''),'-',''),'(',''),')',''))<>11"+(string.IsNullOrWhiteSpace(quartel)?"":" AND u.Quartel=$q")+$" GROUP BY u.Id ORDER BY COUNT(p.UsuarioId) DESC,CASE WHEN COUNT(p.UsuarioId)=0 THEN {hierarchy} ELSE 0 END,u.Nome";
            c.Parameters.AddWithValue("$i",i); c.Parameters.AddWithValue("$f",f); if(!string.IsNullOrWhiteSpace(quartel)) c.Parameters.AddWithValue("$q",quartel); if(!string.IsNullOrWhiteSpace(natureza)) c.Parameters.AddWithValue("$n",natureza);
            using var r=c.ExecuteReader(); while(r.Read()) ranking.Add(new ContagemResumo(r.GetString(0),r.GetInt64(1)));
        }
        return new EstatisticasResumo(tcoCount,otherCount,naturezas,ranking);
    }
    public void ReactivateUsuario(long id,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Usuarios SET Situacao='ATIVO' WHERE Id=$id AND Oculto=0";c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();Log(actor,"USUÁRIO",$"Usuário #{id} reativado.");}
    public void ReactivateQuartel(long id,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Quarteis SET Situacao='ATIVO' WHERE Id=$id";c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();Log(actor,"QUARTEL",$"Quartel #{id} reativado.");}
    public void ReactivateNatureza(long id,UserSession actor){using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE Naturezas SET Situacao='ATIVO' WHERE Id=$id";c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();Log(actor,"NATUREZA",$"Natureza #{id} reativada.");}
    public List<ParticipacaoResumo> GetParticipacoes(long userId,DateTime inicio,DateTime fim,string? quartel,string tipo,string? natureza){using var db=Connect();string q=" AND DataFato BETWEEN $i AND $f";var pieces=new List<string>();if(tipo!="OUTRAS OCORRÊNCIAS GERAIS")pieces.Add("SELECT 'TCO',t.Numero,t.DataFato,t.CriadoEm,t.CriadoPor FROM TcoGuarnicao g JOIN Tcos t ON t.Id=g.TcoId WHERE g.UsuarioId=$u"+q+" AND t.Status='ATIVO'"+(string.IsNullOrWhiteSpace(natureza)?"":" AND EXISTS(SELECT 1 FROM TcoNaturezas x JOIN Naturezas n ON n.Id=x.NaturezaId WHERE x.TcoId=t.Id AND n.Descricao=$n)"));if(tipo!="TCO")pieces.Add("SELECT 'OUTRA OCORRÊNCIA',o.Numero,o.DataFato,o.CriadoEm,o.CriadoPor FROM OutrasGuarnicao g JOIN OutrasOcorrencias o ON o.Id=g.OcorrenciaId WHERE g.UsuarioId=$u"+q+" AND o.Status='ATIVO'"+(string.IsNullOrWhiteSpace(natureza)?"":" AND o.Natureza=$n"));using var c=db.CreateCommand();c.CommandText=string.Join(" UNION ALL ",pieces)+" ORDER BY 3 DESC";c.Parameters.AddWithValue("$u",userId);c.Parameters.AddWithValue("$i",inicio.ToString("yyyy-MM-dd"));c.Parameters.AddWithValue("$f",fim.ToString("yyyy-MM-dd"));if(!string.IsNullOrWhiteSpace(natureza))c.Parameters.AddWithValue("$n",natureza);using var r=c.ExecuteReader();var x=new List<ParticipacaoResumo>();while(r.Read()){var dataCadastro=r.GetString(3);if(DateTime.TryParse(dataCadastro,out var data))dataCadastro=data.ToString("dd/MM/yyyy HH:mm");x.Add(new ParticipacaoResumo(r.GetString(0),r.GetString(1),r.GetString(2),dataCadastro,r.GetString(4)));}return x;}
    public void CreateTco(string numero,string quartel,string origem,DateTime data,string hora,IEnumerable<Natureza> naturezas,IEnumerable<Policial> guarnicao,string observacao,UserSession user)
    { using var db=Connect(); using var tx=db.BeginTransaction(); using var c=db.CreateCommand(); c.Transaction=tx; c.CommandText="INSERT INTO Tcos(Numero,Quartel,Origem,DataFato,HoraFato,Observacao,CriadoEm,CriadoPor) VALUES($n,$q,$o,$d,$h,$x,$c,$u); SELECT last_insert_rowid();"; c.Parameters.AddWithValue("$n",numero);c.Parameters.AddWithValue("$q",quartel);c.Parameters.AddWithValue("$o",origem);c.Parameters.AddWithValue("$d",data.ToString("yyyy-MM-dd"));c.Parameters.AddWithValue("$h",hora);c.Parameters.AddWithValue("$x",observacao);c.Parameters.AddWithValue("$c",DateTime.Now.ToString("O"));c.Parameters.AddWithValue("$u",user.Login);var id=(long)c.ExecuteScalar()!; foreach(var item in naturezas){using var n=db.CreateCommand();n.Transaction=tx;n.CommandText="INSERT OR IGNORE INTO TcoNaturezas(TcoId,NaturezaId) VALUES($id,$natureza)";n.Parameters.AddWithValue("$id",id);n.Parameters.AddWithValue("$natureza",item.Id);n.ExecuteNonQuery();}foreach(var person in guarnicao){using var g=db.CreateCommand();g.Transaction=tx;g.CommandText="INSERT OR IGNORE INTO TcoGuarnicao(TcoId,UsuarioId) VALUES($id,$usuario)";g.Parameters.AddWithValue("$id",id);g.Parameters.AddWithValue("$usuario",person.Id);g.ExecuteNonQuery();}tx.Commit();Log(user,"TCO",$"TCO {numero} cadastrado."); }
    public void CreateOther(string numero,string quartel,DateTime data,string hora,string natureza,IEnumerable<Policial> guarnicao,string material,string observacao,UserSession user)
    { using var db=Connect();using var tx=db.BeginTransaction();using var c=db.CreateCommand();c.Transaction=tx;c.CommandText="INSERT INTO OutrasOcorrencias(Numero,Quartel,DataFato,HoraFato,Natureza,MaterialApreendido,Observacao,CriadoEm,CriadoPor) VALUES($n,$q,$d,$h,$t,$m,$o,$c,$u); SELECT last_insert_rowid();";c.Parameters.AddWithValue("$n",numero);c.Parameters.AddWithValue("$q",quartel);c.Parameters.AddWithValue("$d",data.ToString("yyyy-MM-dd"));c.Parameters.AddWithValue("$h",hora);c.Parameters.AddWithValue("$t",natureza);c.Parameters.AddWithValue("$m",material);c.Parameters.AddWithValue("$o",observacao);c.Parameters.AddWithValue("$c",DateTime.Now.ToString("O"));c.Parameters.AddWithValue("$u",user.Login);var id=(long)c.ExecuteScalar()!;foreach(var person in guarnicao){using var g=db.CreateCommand();g.Transaction=tx;g.CommandText="INSERT OR IGNORE INTO OutrasGuarnicao(OcorrenciaId,UsuarioId) VALUES($id,$usuario)";g.Parameters.AddWithValue("$id",id);g.Parameters.AddWithValue("$usuario",person.Id);g.ExecuteNonQuery();}tx.Commit();Log(user,"OUTRA OCORRÊNCIA",$"Ocorrência {numero} cadastrada."); }
    public TcoEdicao? GetTcoForEdit(long id)
    {
        using var db=Connect();
        long recordId; string numero, quartel, origem, hora, observacao, criadoEm, criadoPor, status; DateTime data;
        using (var c=db.CreateCommand())
        {
            c.CommandText="SELECT Id,Numero,Quartel,Origem,DataFato,HoraFato,IFNULL(Observacao,''),CriadoEm,CriadoPor,Status FROM Tcos WHERE Id=$id";
            c.Parameters.AddWithValue("$id",id);
            using var r=c.ExecuteReader();
            if(!r.Read())return null;
            recordId=r.GetInt64(0); numero=r.GetString(1); quartel=r.GetString(2); origem=r.GetString(3);
            data=DateTime.Parse(r.GetString(4)); hora=r.GetString(5); observacao=r.GetString(6);
            criadoEm=r.GetString(7); criadoPor=r.GetString(8); status=r.GetString(9);
        }
        var naturezas=GetTcoNaturezas(db,recordId);
        var guarnicao=GetTcoGuarnicao(db,recordId);
        return new TcoEdicao(recordId,numero,quartel,origem,data,hora,observacao,criadoEm,criadoPor,status,naturezas,guarnicao);
    }
    public OcorrenciaEdicao? GetOtherForEdit(long id)
    {
        using var db=Connect();
        long recordId; string numero, quartel, hora, natureza, material, observacao, criadoEm, criadoPor, status; DateTime data;
        using (var c=db.CreateCommand())
        {
            c.CommandText="SELECT Id,Numero,Quartel,DataFato,HoraFato,Natureza,IFNULL(MaterialApreendido,''),IFNULL(Observacao,''),CriadoEm,CriadoPor,Status FROM OutrasOcorrencias WHERE Id=$id";
            c.Parameters.AddWithValue("$id",id);
            using var r=c.ExecuteReader();
            if(!r.Read())return null;
            recordId=r.GetInt64(0); numero=r.GetString(1); quartel=r.GetString(2); data=DateTime.Parse(r.GetString(3));
            hora=r.GetString(4); natureza=r.GetString(5); material=r.GetString(6); observacao=r.GetString(7);
            criadoEm=r.GetString(8); criadoPor=r.GetString(9); status=r.GetString(10);
        }
        var guarnicao=GetOtherGuarnicao(db,recordId);
        return new OcorrenciaEdicao(recordId,numero,quartel,data,hora,natureza,material,observacao,criadoEm,criadoPor,status,guarnicao);
    }
    public bool CanEdit(string criadoEm,string criadoPor,UserSession user) => user.Role=="ADMINISTRADOR" || (user.Login==criadoPor && DateTime.TryParse(criadoEm,out var at) && DateTime.Now<=at.AddHours(24));
    public void UpdateTco(TcoEdicao old,string numero,string quartel,string origem,DateTime data,string hora,IEnumerable<Natureza> naturezas,IEnumerable<Policial> guarnicao,string observacao,UserSession user)
    { using var db=Connect();using var tx=db.BeginTransaction();using var c=db.CreateCommand();c.Transaction=tx;c.CommandText="UPDATE Tcos SET Numero=$n,Quartel=$q,Origem=$o,DataFato=$d,HoraFato=$h,Observacao=$x WHERE Id=$id";c.Parameters.AddWithValue("$n",numero);c.Parameters.AddWithValue("$q",quartel);c.Parameters.AddWithValue("$o",origem);c.Parameters.AddWithValue("$d",data.ToString("yyyy-MM-dd"));c.Parameters.AddWithValue("$h",hora);c.Parameters.AddWithValue("$x",observacao);c.Parameters.AddWithValue("$id",old.Id);c.ExecuteNonQuery();ReplaceTcoLinks(db,tx,old.Id,naturezas,guarnicao);History(db,tx,"TCO",old.Id,user,"EDIÇÃO",$"TCO {numero} alterado.");tx.Commit();Log(user,"TCO",$"TCO {numero} alterado."); }
    public void UpdateOther(OcorrenciaEdicao old,string numero,string quartel,DateTime data,string hora,string natureza,IEnumerable<Policial> guarnicao,string material,string observacao,UserSession user)
    { using var db=Connect();using var tx=db.BeginTransaction();using var c=db.CreateCommand();c.Transaction=tx;c.CommandText="UPDATE OutrasOcorrencias SET Numero=$n,Quartel=$q,DataFato=$d,HoraFato=$h,Natureza=$t,MaterialApreendido=$m,Observacao=$o WHERE Id=$id";c.Parameters.AddWithValue("$n",numero);c.Parameters.AddWithValue("$q",quartel);c.Parameters.AddWithValue("$d",data.ToString("yyyy-MM-dd"));c.Parameters.AddWithValue("$h",hora);c.Parameters.AddWithValue("$t",natureza);c.Parameters.AddWithValue("$m",material);c.Parameters.AddWithValue("$o",observacao);c.Parameters.AddWithValue("$id",old.Id);c.ExecuteNonQuery();using var del=db.CreateCommand();del.Transaction=tx;del.CommandText="DELETE FROM OutrasGuarnicao WHERE OcorrenciaId=$id";del.Parameters.AddWithValue("$id",old.Id);del.ExecuteNonQuery();foreach(var p in guarnicao){using var g=db.CreateCommand();g.Transaction=tx;g.CommandText="INSERT INTO OutrasGuarnicao(OcorrenciaId,UsuarioId) VALUES($id,$u)";g.Parameters.AddWithValue("$id",old.Id);g.Parameters.AddWithValue("$u",p.Id);g.ExecuteNonQuery();}History(db,tx,"OUTRA OCORRÊNCIA",old.Id,user,"EDIÇÃO",$"Ocorrência {numero} alterada.");tx.Commit();Log(user,"OUTRA OCORRÊNCIA",$"Ocorrência {numero} alterada."); }
    public void Inactivate(string modulo,long id,string motivo,UserSession user)
    { if(user.Role!="ADMINISTRADOR")throw new InvalidOperationException("Somente administrador pode inativar registros.");using var db=Connect();using var tx=db.BeginTransaction();using var c=db.CreateCommand();c.Transaction=tx;c.CommandText=modulo=="TCO"?"UPDATE Tcos SET Status='INATIVO',InativadoMotivo=$m WHERE Id=$id":"UPDATE OutrasOcorrencias SET Status='INATIVO',InativadoMotivo=$m WHERE Id=$id";c.Parameters.AddWithValue("$m",motivo);c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();History(db,tx,modulo,id,user,"INATIVAÇÃO",motivo);tx.Commit();Log(user,modulo,$"Registro #{id} inativado: {motivo}"); }
    public void ReactivateRegistro(string modulo,long id,UserSession user){if(user.Role!="ADMINISTRADOR")throw new InvalidOperationException("Somente administrador pode reativar registros.");using var db=Connect();using var c=db.CreateCommand();c.CommandText=modulo=="TCO"?"UPDATE Tcos SET Status='ATIVO',InativadoMotivo=NULL WHERE Id=$id":"UPDATE OutrasOcorrencias SET Status='ATIVO',InativadoMotivo=NULL WHERE Id=$id";c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();Log(user,modulo,$"Registro #{id} reativado.");}
    public void DeleteTco(long id,UserSession user){if(user.Role!="ADMINISTRADOR")throw new InvalidOperationException("Somente administrador pode excluir TCO.");using var db=Connect();using var check=db.CreateCommand();check.CommandText="SELECT Numero FROM Tcos WHERE Id=$id AND Status='INATIVO'";check.Parameters.AddWithValue("$id",id);var numero=check.ExecuteScalar() as string;if(numero is null)throw new InvalidOperationException("Somente TCO inativo pode ser excluído.");using var tx=db.BeginTransaction();using var c=db.CreateCommand();c.Transaction=tx;c.CommandText="DELETE FROM TcoNaturezas WHERE TcoId=$id; DELETE FROM TcoGuarnicao WHERE TcoId=$id; DELETE FROM Tcos WHERE Id=$id";c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();tx.Commit();Log(user,"TCO",$"TCO {numero} excluído definitivamente após inativação.");}
    public List<TcoResumo> GetRecentTcos(string? pesquisa=null,string? natureza=null){using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT t.Id,t.Numero,t.DataFato,t.HoraFato,t.Quartel,t.Status,IFNULL(group_concat(n.Artigo || ' — ' || n.Descricao, '; '),''),IFNULL((SELECT group_concat(u.PostoGraduacao || ' PM ' || u.Nome, '; ') FROM TcoGuarnicao g JOIN Usuarios u ON u.Id=g.UsuarioId WHERE g.TcoId=t.Id),'') FROM Tcos t LEFT JOIN TcoNaturezas tn ON tn.TcoId=t.Id LEFT JOIN Naturezas n ON n.Id=tn.NaturezaId WHERE ($p='' OR t.Numero LIKE $like OR t.Quartel LIKE $like OR n.Descricao LIKE $like) AND ($natureza='' OR EXISTS(SELECT 1 FROM TcoNaturezas filtroTco JOIN Naturezas filtroNatureza ON filtroNatureza.Id=filtroTco.NaturezaId WHERE filtroTco.TcoId=t.Id AND filtroNatureza.Descricao=$natureza)) GROUP BY t.Id ORDER BY t.DataFato DESC,t.HoraFato DESC,t.CriadoEm DESC";c.Parameters.AddWithValue("$p",pesquisa??"");c.Parameters.AddWithValue("$like","%"+(pesquisa??"")+"%");c.Parameters.AddWithValue("$natureza",natureza??"");using var r=c.ExecuteReader();var x=new List<TcoResumo>();while(r.Read())x.Add(new TcoResumo(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7)));return x;}
    public List<OcorrenciaResumo> GetRecentOthers(string? natureza=null,string? pesquisa=null){using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT Id,Numero,DataFato,HoraFato,Quartel,Natureza,Status FROM OutrasOcorrencias WHERE ($natureza='' OR Natureza=$natureza) AND ($pesquisa='' OR Numero LIKE $like OR Quartel LIKE $like OR Natureza LIKE $like) ORDER BY DataFato DESC,HoraFato DESC,CriadoEm DESC LIMIT 10";c.Parameters.AddWithValue("$natureza",natureza??"");c.Parameters.AddWithValue("$pesquisa",pesquisa??"");c.Parameters.AddWithValue("$like","%"+(pesquisa??"")+"%");using var r=c.ExecuteReader();var x=new List<OcorrenciaResumo>();while(r.Read())x.Add(new OcorrenciaResumo(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6)));return x;}
    public TcoDetalhe? GetTco(long id){using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT t.Numero,t.Quartel,t.Origem,t.DataFato,t.HoraFato,t.Status,IFNULL(t.Observacao,''),IFNULL(group_concat(DISTINCT n.Artigo || ' — ' || n.Descricao),''),IFNULL((SELECT group_concat(u.PostoGraduacao || ' PM ' || u.Nome, '; ') FROM TcoGuarnicao g JOIN Usuarios u ON u.Id=g.UsuarioId WHERE g.TcoId=t.Id),'') FROM Tcos t LEFT JOIN TcoNaturezas tn ON tn.TcoId=t.Id LEFT JOIN Naturezas n ON n.Id=tn.NaturezaId WHERE t.Id=$id GROUP BY t.Id";c.Parameters.AddWithValue("$id",id);using var r=c.ExecuteReader();return r.Read()?new TcoDetalhe(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8)):null;}
    public OcorrenciaDetalhe? GetOther(long id){using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT o.Numero,o.Quartel,o.DataFato,o.HoraFato,o.Natureza,o.Status,IFNULL(o.MaterialApreendido,''),IFNULL(o.Observacao,''),IFNULL((SELECT group_concat(u.PostoGraduacao || ' PM ' || u.Nome, '; ') FROM OutrasGuarnicao g JOIN Usuarios u ON u.Id=g.UsuarioId WHERE g.OcorrenciaId=o.Id),'') FROM OutrasOcorrencias o WHERE o.Id=$id";c.Parameters.AddWithValue("$id",id);using var r=c.ExecuteReader();return r.Read()?new OcorrenciaDetalhe(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8)):null;}
    private string RequireActive() => GetActiveDatabase() ?? throw new InvalidOperationException("Selecione ou crie um banco antes de acessar o sistema.");
    private static bool IsDatabaseInDataDirectory(string database)
    {
        if (string.IsNullOrWhiteSpace(database)) return false;
        var dataDirectory = Path.GetFullPath(DataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(database);
        return fullPath.StartsWith(dataDirectory, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Path.GetExtension(fullPath), ".db", StringComparison.OrdinalIgnoreCase);
    }
    private static string CreateDatabasePath()
    {
        var stamp = DateTime.Today.ToString("yyyy-MM-dd");
        var path = Path.Combine(DataDirectory, $"estatisticas_15bpm_{stamp}.db");
        var n = 2;
        while (File.Exists(path)) path = Path.Combine(DataDirectory, $"estatisticas_15bpm_{stamp}_{n++}.db");
        return path;
    }
    private static void WriteActiveDatabase(string database)
    {
        var temporary = ActiveFile + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, database);
            File.Move(temporary, ActiveFile, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    private static void ConfigureConnection(SqliteConnection database)
    {
        using var command = database.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
    }
    private static void CreateConsistentBackup(string source, string destination)
    {
        if (!File.Exists(source)) throw new InvalidOperationException("Banco de dados não encontrado para criação do backup.");
        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (string.IsNullOrWhiteSpace(destinationDirectory)) throw new InvalidOperationException("Destino de backup inválido.");
        Directory.CreateDirectory(destinationDirectory);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using var sourceDatabase = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            using var destinationDatabase = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString());
            sourceDatabase.Open();
            ConfigureConnection(sourceDatabase);
            destinationDatabase.Open();
            ConfigureConnection(destinationDatabase);
            sourceDatabase.BackupDatabase(destinationDatabase);
            destinationDatabase.Close();
            sourceDatabase.Close();
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    private static void ValidateImport(string source)
    {
        if (!File.Exists(source)) throw new InvalidOperationException("Arquivo de backup não encontrado.");
        try
        {
            using var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly }.ToString());
            database.Open();
            ConfigureConnection(database);
            using var integrity = database.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check";
            if (!string.Equals(integrity.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O arquivo informado possui falha de integridade.");
            using var tables = database.CreateCommand();
            tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('Usuarios','Quarteis','Naturezas','Tcos','OutrasOcorrencias')";
            using var reader = tables.ExecuteReader();
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read()) found.Add(reader.GetString(0));
            if (found.Count != 5) throw new InvalidOperationException("O arquivo não é um backup válido do ESTATÍSTICAS 15ª BPM.");
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException("O arquivo selecionado não pôde ser validado como banco de dados.", ex);
        }
    }
    private static void DeleteDatabaseFiles(string database)
    {
        foreach (var path in new[] { database, database + "-wal", database + "-shm" })
            if (File.Exists(path)) File.Delete(path);
    }
    private static void ApplyApprovedStaffUpdate(string active)
    {
        if (EfetivoAgosto2026.IsApplied(active)) return;
        var backup = Path.Combine(BackupDirectory, $"Backup_15BPM_antes_atualizacao_efetivo_{DateTime.Now:yyyy-MM-dd_HHmmss}.bkp15");
        CreateConsistentBackup(active, backup);
        EfetivoAgosto2026.Apply(active, Hash, backup);
    }
    private static void ApplyCleanupMigration(string database)
    {
        using var db=new SqliteConnection($"Data Source={database}");
        db.Open();
        ConfigureConnection(db);
        using var check=db.CreateCommand();
        check.CommandText="SELECT COUNT(*) FROM VersoesBanco WHERE Versao=1001";
        if(Convert.ToInt64(check.ExecuteScalar())>0)return;

        long CountRows(string table)
        {
            using var exists=db.CreateCommand();
            exists.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
            exists.Parameters.AddWithValue("$table",table);
            if(Convert.ToInt64(exists.ExecuteScalar())==0)return 0;
            using var count=db.CreateCommand();
            count.CommandText=$"SELECT COUNT(*) FROM {table}";
            return Convert.ToInt64(count.ExecuteScalar());
        }

        var pending=CountRows("Pendencias");
        var exclusions=CountRows("HistoricoExclusoes");
        if(pending>0||exclusions>0)return;

        var backup=Path.Combine(BackupDirectory,$"Backup_15BPM_antes_limpeza_estrutura_{DateTime.Now:yyyy-MM-dd_HHmmss}.bkp15");
        CreateConsistentBackup(database,backup);
        using var tx=db.BeginTransaction();
        using var migration=db.CreateCommand();
        migration.Transaction=tx;
        migration.CommandText="DROP TABLE IF EXISTS Pendencias; DROP TABLE IF EXISTS HistoricoExclusoes; INSERT INTO VersoesBanco(Versao,AplicadaEm) VALUES(1001,$data)";
        migration.Parameters.AddWithValue("$data",DateTime.UtcNow.ToString("O"));
        migration.ExecuteNonQuery();
        tx.Commit();
    }
    private static void Initialize(string path)
    {
        using var db = new SqliteConnection($"Data Source={path}"); db.Open(); ConfigureConnection(db); using var tx = db.BeginTransaction(); using var c = db.CreateCommand(); c.Transaction=tx;
        c.CommandText = @"
CREATE TABLE IF NOT EXISTS Usuarios(Id INTEGER PRIMARY KEY,Login TEXT UNIQUE NOT NULL,Documento TEXT UNIQUE,Nome TEXT NOT NULL,PostoGraduacao TEXT,Quartel TEXT,Funcao TEXT NOT NULL,Situacao TEXT NOT NULL DEFAULT 'ATIVO',SenhaHash TEXT NOT NULL,Oculto INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS Quarteis(Id INTEGER PRIMARY KEY,Nome TEXT UNIQUE NOT NULL,Situacao TEXT NOT NULL DEFAULT 'ATIVO');
CREATE TABLE IF NOT EXISTS Naturezas(Id INTEGER PRIMARY KEY,Modulo TEXT NOT NULL,Artigo TEXT,Descricao TEXT NOT NULL,Situacao TEXT NOT NULL DEFAULT 'ATIVO',Aprovada INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS Tcos(Id INTEGER PRIMARY KEY,Numero TEXT UNIQUE NOT NULL,Quartel TEXT NOT NULL,Origem TEXT NOT NULL,DataFato TEXT NOT NULL,HoraFato TEXT NOT NULL,Status TEXT NOT NULL DEFAULT 'ATIVO',Observacao TEXT,CriadoEm TEXT NOT NULL,CriadoPor TEXT NOT NULL,InativadoMotivo TEXT);
CREATE TABLE IF NOT EXISTS TcoNaturezas(TcoId INTEGER NOT NULL REFERENCES Tcos(Id),NaturezaId INTEGER NOT NULL REFERENCES Naturezas(Id),PRIMARY KEY(TcoId,NaturezaId));
CREATE TABLE IF NOT EXISTS TcoGuarnicao(TcoId INTEGER NOT NULL REFERENCES Tcos(Id),UsuarioId INTEGER NOT NULL REFERENCES Usuarios(Id),PRIMARY KEY(TcoId,UsuarioId));
CREATE TABLE IF NOT EXISTS OutrasOcorrencias(Id INTEGER PRIMARY KEY,Numero TEXT UNIQUE NOT NULL,Quartel TEXT NOT NULL,DataFato TEXT NOT NULL,HoraFato TEXT NOT NULL,Natureza TEXT NOT NULL,MaterialApreendido TEXT,Observacao TEXT,Status TEXT NOT NULL DEFAULT 'ATIVO',CriadoEm TEXT NOT NULL,CriadoPor TEXT NOT NULL,InativadoMotivo TEXT);
CREATE TABLE IF NOT EXISTS OutrasGuarnicao(OcorrenciaId INTEGER NOT NULL REFERENCES OutrasOcorrencias(Id),UsuarioId INTEGER NOT NULL REFERENCES Usuarios(Id),PRIMARY KEY(OcorrenciaId,UsuarioId));
CREATE TABLE IF NOT EXISTS Logs(Id INTEGER PRIMARY KEY,DataHora TEXT NOT NULL,Usuario TEXT NOT NULL,Funcao TEXT NOT NULL,Registro TEXT NOT NULL,Descricao TEXT NOT NULL,Permanente INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS HistoricoRegistros(Id INTEGER PRIMARY KEY,Modulo TEXT NOT NULL,RegistroId INTEGER NOT NULL,DataHora TEXT NOT NULL,Usuario TEXT NOT NULL,Acao TEXT NOT NULL,Descricao TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS VersoesBanco(Versao INTEGER PRIMARY KEY,AplicadaEm TEXT NOT NULL);
INSERT OR IGNORE INTO Quarteis(Nome) VALUES('SEDE'),('ALTO GARÇAS'),('ALTO TAQUARI'),('ARAGUAINHA'),('PONTE BRANCA');
INSERT OR IGNORE INTO Quarteis(Nome) VALUES('15º BPM - SEDE');
UPDATE Usuarios SET Quartel='15º BPM - SEDE' WHERE Quartel='SEDE';
DELETE FROM Quarteis WHERE Nome='SEDE';
INSERT OR IGNORE INTO TcoNaturezas(TcoId,NaturezaId)
SELECT tn.TcoId,(SELECT MIN(n2.Id) FROM Naturezas n2 WHERE n2.Modulo=n.Modulo AND IFNULL(n2.Artigo,'')=IFNULL(n.Artigo,'') AND n2.Descricao=n.Descricao)
FROM TcoNaturezas tn JOIN Naturezas n ON n.Id=tn.NaturezaId
WHERE n.Id<>(SELECT MIN(n2.Id) FROM Naturezas n2 WHERE n2.Modulo=n.Modulo AND IFNULL(n2.Artigo,'')=IFNULL(n.Artigo,'') AND n2.Descricao=n.Descricao);
DELETE FROM TcoNaturezas WHERE NaturezaId IN (SELECT n.Id FROM Naturezas n WHERE n.Id<>(SELECT MIN(n2.Id) FROM Naturezas n2 WHERE n2.Modulo=n.Modulo AND IFNULL(n2.Artigo,'')=IFNULL(n.Artigo,'') AND n2.Descricao=n.Descricao));
DELETE FROM Naturezas WHERE Id IN (SELECT n.Id FROM Naturezas n WHERE n.Id<>(SELECT MIN(n2.Id) FROM Naturezas n2 WHERE n2.Modulo=n.Modulo AND IFNULL(n2.Artigo,'')=IFNULL(n.Artigo,'') AND n2.Descricao=n.Descricao));
CREATE UNIQUE INDEX IF NOT EXISTS IX_Naturezas_Unica ON Naturezas(Modulo,Artigo,Descricao);
INSERT OR IGNORE INTO Naturezas(Modulo,Artigo,Descricao,Aprovada) VALUES
('TCO','Art. 21','Vias de fato',1),('TCO','Art. 42','Perturbação do trabalho ou do sossego alheios',1),('TCO','Art. 47','Exercício ilegal de profissão ou atividade',1),('TCO','Art. 50','Jogo de azar',1),('TCO','Art. 62','Embriaguez',1),('TCO','Art. 68','Recusa de dados sobre identidade/qualificação',1),('TCO','Art. 129','Lesão corporal',1),('TCO','Art. 140','Injúria',1),('TCO','Art. 147','Ameaça',1),('TCO','Art. 147-A','Perseguição',1),('TCO','Art. 150','Violação de domicílio',1),('TCO','Art. 163','Dano',1),('TCO','Art. 309 — CTB','Dirigir sem habilitação gerando perigo de dano',1),('TCO','Art. 310 — CTB','Entregar/confiar/permitir veículo a pessoa não habilitada',1),('TCO','Art. 311 — CTB','Velocidade incompatível gerando perigo de dano',1),('TCO','Art. 330','Desobediência',1),('TCO','Art. 331','Desacato',1);
INSERT OR IGNORE INTO Naturezas(Modulo,Artigo,Descricao,Aprovada) VALUES('CRIMES','','ARMA APREENDIDA',1),('CRIMES','','TRÁFICO / USO DE ENTORPECENTE',1),('CRIMES','','ROUBO',1),('CRIMES','','HOMÍCIDIO',1),('CRIMES','','PRISÃO POR MANDATO',1),('CRIMES','','DESCAMINHO / CONTRABANDO',1);
INSERT OR IGNORE INTO Usuarios(Login,Nome,Funcao,Situacao,SenhaHash,Oculto) VALUES('admin','Administrador Técnico','ADMINISTRADOR','ATIVO',$hash,1);";
        c.Parameters.AddWithValue("$hash", Hash("admin")); c.ExecuteNonQuery();
        try { using var migration=db.CreateCommand();migration.Transaction=tx;migration.CommandText="ALTER TABLE Usuarios ADD COLUMN SenhaInicial INTEGER NOT NULL DEFAULT 0";migration.ExecuteNonQuery(); } catch (SqliteException) { }
        using var u=db.CreateCommand();u.Transaction=tx;u.CommandText="INSERT OR IGNORE INTO Usuarios(Login,Documento,Nome,PostoGraduacao,Quartel,Funcao,Situacao,SenhaHash,Oculto) VALUES $values";var people=new[]{("882084","ESTRADA","SUBTENENTE"),("880378","MANGABEIRA","SUBTENENTE"),("881831","BASTO","1º SARGENTO"),("882406","BENEDITO","1º SARGENTO"),("883599","DANIEL","1º SARGENTO"),("882872","FRAGA","1º SARGENTO"),("881865","LUIS HENRIQUE","1º SARGENTO"),("882914","SILVA RESENDE","1º SARGENTO"),("884069","GONCALVES","2º SARGENTO"),("885240","PEIXOTO","3º SARGENTO"),("885298","RENATO","3º SARGENTO"),("886487","HENAN ALVES","CABO"),("887454","LUIZ FELIPE","CABO"),("886860","RONILSON","CABO"),("887906","FERNANDES","SOLDADO"),("887911","GUINNYWER","SOLDADO"),("888338","LEONARDO","SOLDADO"),("888029","LORAINNY","SOLDADO"),("888237","ROMEIRO","SOLDADO"),("887793","SEVERO","SOLDADO"),("888228","TELES","SOLDADO")};var sets=new List<string>();for(var i=0;i<people.Length;i++){sets.Add($"($l{i},$d{i},$n{i},$p{i},'SEDE','OPERADOR','ATIVO',$pw,0)");u.Parameters.AddWithValue($"$l{i}",people[i].Item1);u.Parameters.AddWithValue($"$d{i}",people[i].Item1);u.Parameters.AddWithValue($"$n{i}",people[i].Item2);u.Parameters.AddWithValue($"$p{i}",people[i].Item3);}u.Parameters.AddWithValue("$pw",Hash("15bpm"));u.CommandText=u.CommandText.Replace("$values",string.Join(',',sets));u.ExecuteNonQuery();using var renameUsers=db.CreateCommand();renameUsers.Transaction=tx;renameUsers.CommandText="UPDATE Usuarios SET Quartel='15º BPM - SEDE' WHERE Quartel='SEDE'";renameUsers.ExecuteNonQuery();using var version=db.CreateCommand();version.Transaction=tx;version.CommandText="INSERT OR IGNORE INTO VersoesBanco(Versao,AplicadaEm) VALUES(1,$data)";version.Parameters.AddWithValue("$data",DateTime.UtcNow.ToString("O"));version.ExecuteNonQuery();tx.Commit();
    }
    private static List<Natureza> GetTcoNaturezas(SqliteConnection db,long id){using var c=db.CreateCommand();c.CommandText="SELECT n.Id,n.Artigo,n.Descricao FROM Naturezas n JOIN TcoNaturezas t ON t.NaturezaId=n.Id WHERE t.TcoId=$id";c.Parameters.AddWithValue("$id",id);using var r=c.ExecuteReader();var x=new List<Natureza>();while(r.Read())x.Add(new Natureza(r.GetInt64(0),r.IsDBNull(1)?"":r.GetString(1),r.GetString(2)));return x;}
    private static List<Policial> GetTcoGuarnicao(SqliteConnection db,long id)=>GetGuarnicao(db,"SELECT u.Id,u.PostoGraduacao,u.Nome,u.Login,u.Quartel FROM Usuarios u JOIN TcoGuarnicao g ON g.UsuarioId=u.Id WHERE g.TcoId=$id",id);
    private static List<Policial> GetOtherGuarnicao(SqliteConnection db,long id)=>GetGuarnicao(db,"SELECT u.Id,u.PostoGraduacao,u.Nome,u.Login,u.Quartel FROM Usuarios u JOIN OutrasGuarnicao g ON g.UsuarioId=u.Id WHERE g.OcorrenciaId=$id",id);
    private static List<Policial> GetGuarnicao(SqliteConnection db,string sql,long id){using var c=db.CreateCommand();c.CommandText=sql;c.Parameters.AddWithValue("$id",id);using var r=c.ExecuteReader();var x=new List<Policial>();while(r.Read())x.Add(new Policial(r.GetInt64(0),r.IsDBNull(1)?"":r.GetString(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?"":r.GetString(4)));return x;}
    private static void ReplaceTcoLinks(SqliteConnection db,SqliteTransaction tx,long id,IEnumerable<Natureza> natures,IEnumerable<Policial> people){using(var d=db.CreateCommand()){d.Transaction=tx;d.CommandText="DELETE FROM TcoNaturezas WHERE TcoId=$id; DELETE FROM TcoGuarnicao WHERE TcoId=$id";d.Parameters.AddWithValue("$id",id);d.ExecuteNonQuery();}foreach(var n in natures){using var c=db.CreateCommand();c.Transaction=tx;c.CommandText="INSERT INTO TcoNaturezas(TcoId,NaturezaId) VALUES($id,$n)";c.Parameters.AddWithValue("$id",id);c.Parameters.AddWithValue("$n",n.Id);c.ExecuteNonQuery();}foreach(var p in people){using var c=db.CreateCommand();c.Transaction=tx;c.CommandText="INSERT INTO TcoGuarnicao(TcoId,UsuarioId) VALUES($id,$u)";c.Parameters.AddWithValue("$id",id);c.Parameters.AddWithValue("$u",p.Id);c.ExecuteNonQuery();}}
    private static void History(SqliteConnection db,SqliteTransaction tx,string modulo,long id,UserSession user,string acao,string descricao){using var c=db.CreateCommand();c.Transaction=tx;c.CommandText="INSERT INTO HistoricoRegistros(Modulo,RegistroId,DataHora,Usuario,Acao,Descricao) VALUES($m,$id,$d,$u,$a,$x)";c.Parameters.AddWithValue("$m",modulo);c.Parameters.AddWithValue("$id",id);c.Parameters.AddWithValue("$d",DateTime.Now.ToString("O"));c.Parameters.AddWithValue("$u",user.Login);c.Parameters.AddWithValue("$a",acao);c.Parameters.AddWithValue("$x",descricao);c.ExecuteNonQuery();}
    private static string Hash(string v) { var s=RandomNumberGenerator.GetBytes(16); var b=Rfc2898DeriveBytes.Pbkdf2(v,s,100000,HashAlgorithmName.SHA256,32); return Convert.ToBase64String(s)+":"+Convert.ToBase64String(b); }
    private static bool Verify(string v,string h) { var p=h.Split(':'); if(p.Length!=2)return false; var b=Rfc2898DeriveBytes.Pbkdf2(v,Convert.FromBase64String(p[0]),100000,HashAlgorithmName.SHA256,32); return CryptographicOperations.FixedTimeEquals(b,Convert.FromBase64String(p[1])); }
}
public sealed record UserSession(long Id,string Login,string Name,string Role,bool Hidden,bool MustChangePassword);
public sealed record Natureza(long Id,string Artigo,string Descricao) { public string Exibicao => $"{Artigo} — {Descricao}"; }
public sealed record Policial(long Id,string Posto,string Qra,string Rgpm,string Quartel) { public string Exibicao => $"{Posto} PM {Qra} — {Rgpm}"; }
public sealed record TcoResumo(long Id,string Numero,string Data,string Hora,string Quartel,string Status,string Naturezas,string Guarnicao);
public sealed record OcorrenciaResumo(long Id,string Numero,string Data,string Hora,string Quartel,string Natureza,string Status);
public sealed record TcoDetalhe(string Numero,string Quartel,string Origem,string Data,string Hora,string Status,string Observacao,string Naturezas,string Guarnicao);
public sealed record OcorrenciaDetalhe(string Numero,string Quartel,string Data,string Hora,string Natureza,string Status,string Material,string Observacao,string Guarnicao);
public sealed record TcoEdicao(long Id,string Numero,string Quartel,string Origem,DateTime Data,string Hora,string Observacao,string CriadoEm,string CriadoPor,string Status,List<Natureza> Naturezas,List<Policial> Guarnicao);
public sealed record OcorrenciaEdicao(long Id,string Numero,string Quartel,DateTime Data,string Hora,string Natureza,string Material,string Observacao,string CriadoEm,string CriadoPor,string Status,List<Policial> Guarnicao);
public sealed record UsuarioResumo(long Id,string Nome,string Posto,string Login,string Quartel,string Funcao,string Situacao);
public sealed record NaturezaResumo(long Id,string Modulo,string Artigo,string Descricao,string Situacao);
public sealed record LogResumo(string DataHora,string Usuario,string Registro,string Descricao);
public sealed record QuartelResumo(long Id,string Nome,string Situacao);
public sealed record ContagemResumo(string Item,long Quantidade);
public sealed record EstatisticasResumo(long Tcos,long OutrasOcorrencias,List<ContagemResumo> Naturezas,List<ContagemResumo> Ranking);
public sealed record ParticipacaoResumo(string Tipo,string Numero,string DataFato,string DataCadastro,string OperadorCadastro);
public sealed record StorageResumo(long Usado,long Disponivel,long Total);
public sealed record DashboardQuartelResumo(string Quartel,long Quantidade);
public sealed record DashboardRegistroResumo(long Id,string Tipo,string Numero,string Data,string Hora,string Quartel,string Natureza,string Guarnicao);

