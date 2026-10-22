using Microsoft.Data.Sqlite;
using System.IO;

namespace Estatisticas15BPM;

internal static class EfetivoAgosto2026
{
    private const string Sede = "15º BPM - SEDE";
    private const string Dados = """
TEN CEL PM;CÂNDIDO;880783;15º BPM - SEDE;A
MAJ PM;TAVARES;885505;15º BPM - SEDE;A
SUB TEN PM;ESTRADA;882084;15º BPM - SEDE;
SUB TEN PM;MANGABEIRA;880378;15º BPM - SEDE;
SUB TEN PM;BELTSON;879987;15º BPM - SEDE;
1º SGT PM;NOGUEIRA;880979;15º BPM - SEDE;
1º SGT PM;LUIS HENRIQUE;881865;15º BPM - SEDE;
1º SGT PM;ALESSANDRO;881867;15º BPM - SEDE;
1º SGT PM;BASTO;881831;15º BPM - SEDE;
1º SGT PM;ARAUJO;882394;15º BPM - SEDE;
1º SGT PM;BENEDITO;882406;15º BPM - SEDE;
1º SGT PM;FRAGA;882872;15º BPM - SEDE;
1º SGT PM;SILVA RESENDE;882914;15º BPM - SEDE;
1º SGT PM;DANIEL;883599;15º BPM - SEDE;
1º SGT PM;CASSIANO;882906;15º BPM - SEDE;
1º SGT PM;LANDERLAN;883514;15º BPM - SEDE;
2º SGT PM;FRANCESCHI;886578;15º BPM - SEDE;
2º SGT PM;ADELCIONE;884086;15º BPM - SEDE;
2º SGT PM;GONÇALVES;884069;15º BPM - SEDE;
3º SGT PM;PEIXOTO;885240;15º BPM - SEDE;
3º SGT PM;RENATO;885298;15º BPM - SEDE;
3º SGT PM;MAYKEL;884431;15º BPM - SEDE;
CB PM;BEZERRA;885948;15º BPM - SEDE;
CB PM;HENAN;886487;15º BPM - SEDE;
CB PM;SÉRGIO;887792;15º BPM - SEDE;
CB PM;RONILSON;886860;15º BPM - SEDE;
CB PM;WANESSA LOPES;886875;15º BPM - SEDE;
CB PM;LUIZ FELIPE;887454;15º BPM - SEDE;
CB PM;SÍLVIO;886776;15º BPM - SEDE;
CB PM;PAIVA;886753;15º BPM - SEDE;
SD PM;RIBEIRO;886015;15º BPM - SEDE;
SD PM;SEVERO;887793;15º BPM - SEDE;
SD PM;TELES;888228;15º BPM - SEDE;
SD PM;ROMEIRO;888237;15º BPM - SEDE;
SD PM;J. OLIVEIRA;887952;15º BPM - SEDE;
SD PM;FERNANDES;887906;15º BPM - SEDE;
SD PM;GUINNYWER;887911;15º BPM - SEDE;
SD PM;LORAINNY;888029;15º BPM - SEDE;
SD PM;LEONARDO;888338;15º BPM - SEDE;
1º SGT PM RR;PARREIRA;878822;15º BPM - SEDE;R
CB PM RR;LIMA;878516;15º BPM - SEDE;R
CB PM RR;DE SOUZA;880607;15º BPM - SEDE;R
1º SGT PM;ARTHUR;880021;ALTO GARÇAS;
1º SGT PM;G. PEREIRA;880598;ALTO GARÇAS;
1º SGT PM;MENDES;882923;ALTO GARÇAS;
2º SGT PM;DOS SANTOS;884078;ALTO GARÇAS;
3º SGT PM;GALDINO;885209;ALTO GARÇAS;
3º SGT PM;DEIVID;885220;ALTO GARÇAS;
CB PM;ALVES;886044;ALTO GARÇAS;
CB PM;SANTOS LIMA;886816;ALTO GARÇAS;
CB PM;WESLEY;886441;ALTO GARÇAS;
SD PM;MATTOS;888400;ALTO GARÇAS;
SD PM;I. FERREIRA;888000;ALTO GARÇAS;
SD PM;GUSTAVO;888120;ALTO GARÇAS;
SD PM;MENEZES;888225;ALTO GARÇAS;
1º TEN PM;MARCEL;880582;ALTO TAQUARI;
SUB TEN PM;KURY;880030;ALTO TAQUARI;
1º SGT PM;MAIA;882916;ALTO TAQUARI;
1º SGT PM;RAFAEL;885867;ALTO TAQUARI;
1º SGT PM;COSTA;882924;ALTO TAQUARI;
1º SGT PM;VARJÃO;882912;ALTO TAQUARI;
2º SGT PM;REINALDO;881799;ALTO TAQUARI;
2º SGT PM;BELIN;885222;ALTO TAQUARI;
3º SGT PM;CAVASSA;885286;ALTO TAQUARI;
CB PM;FERNANDA;886197;ALTO TAQUARI;
CB PM;LOPES;885221;ALTO TAQUARI;
CB PM;FARIAS;886510;ALTO TAQUARI;
CB PM;DOS SANTOS;887334;ALTO TAQUARI;
CB PM;MURILLO;886845;ALTO TAQUARI;
CB PM;SANTOS BORGES;886822;ALTO TAQUARI;
CB PM;MENDONÇA;886837;ALTO TAQUARI;
SD PM;MODESTO;888526;ALTO TAQUARI;
SUB TEN PM;LEOMAR;880576;ARAGUAINHA;
1º SGT PM;EDUARDO;882908;ARAGUAINHA;
2º SGT PM;VIRGÍLIO;882907;ARAGUAINHA;
3º SGT PM;CARLOS EDUARDO;884091;ARAGUAINHA;
CB PM;SPERAFICO;886800;ARAGUAINHA;
CB PM;WILSON;886852;ARAGUAINHA;
CB PM;RODRIGUES;886836;ARAGUAINHA;
SD PM;SILVA NETO;888322;ARAGUAINHA;
SD PM;ATAIDES;888023;ARAGUAINHA;
SD PM;PASSOS;888189;ARAGUAINHA;
SD PM;MARCOS;888042;ARAGUAINHA;
1º SGT PM;CARLOS;882910;PONTE BRANCA;
1º SGT PM;POMPILIO;882917;PONTE BRANCA;
1º SGT PM;SUQUERE;883034;PONTE BRANCA;
1º SGT PM;UMBELINO;883678;PONTE BRANCA;
2º SGT PM;FERREIRA;884137;PONTE BRANCA;
3º SGT PM;DHANGELLO;885164;PONTE BRANCA;
3º SGT PM;FERNANDO;884430;PONTE BRANCA;
CB PM;LUAN SANTOS;887005;PONTE BRANCA;
CB PM;SENA;887614;PONTE BRANCA;
CB PM;CLÁUDIO;886767;PONTE BRANCA;
SD PM;ALVES;888369;PONTE BRANCA;
SD PM;LIMA;887903;PONTE BRANCA;
""";

    public static bool IsApplied(string path)
    {
        using var db = new SqliteConnection($"Data Source={path}"); db.Open();
        using var command = db.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM VersoesBanco WHERE Versao=2";
        return (long)command.ExecuteScalar()! > 0;
    }

    public static void Apply(string path, Func<string,string> hash, string backupPath)
    {
        var staff = Dados.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.Split(';')).Select(p => new Staff(p[0],p[1],p[2],p[3],p.Length>4 && p[4].Contains('R'),p.Length>4 && p[4].Contains('A'))).ToList();
        if(staff.Count != 95) throw new InvalidOperationException("A relação de efetivo está incompleta.");
        using var db = new SqliteConnection($"Data Source={path}"); db.Open();
        using(var pragma=db.CreateCommand()){pragma.CommandText="PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";pragma.ExecuteNonQuery();}
        var existing = new List<Existing>();
        using(var command=db.CreateCommand()){command.CommandText="SELECT Id,Login,IFNULL(Documento,''),Nome,IFNULL(Quartel,''),Funcao,Situacao FROM Usuarios WHERE Oculto=0";using var reader=command.ExecuteReader();while(reader.Read())existing.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6)));}
        using var tx=db.BeginTransaction(); var added=0;var updated=0;var reactivated=0;
        foreach(var person in staff)
        {
            Existing? target;
            if(person.Rgpm=="888338")
            {
                var old=existing.SingleOrDefault(x=>x.Login=="888338"); var administrator=existing.SingleOrDefault(x=>x.Name=="LEONARDO AMARAL" && x.Role=="ADMINISTRADOR");
                if(old is not null && administrator is not null && old.Id!=administrator.Id){using var exclude=db.CreateCommand();exclude.Transaction=tx;exclude.CommandText="UPDATE Usuarios SET Login='HISTORICO_888338',Documento=NULL,Situacao='EXCLUIDO' WHERE Id=$id";exclude.Parameters.AddWithValue("$id",old.Id);exclude.ExecuteNonQuery();existing.Remove(old);target=administrator;} else target=old??administrator;
            }
            else target=existing.SingleOrDefault(x=>x.Login==person.Rgpm||x.Document==person.Rgpm)??existing.SingleOrDefault(x=>x.Name==person.Qra&&x.Quarter==person.Quarter);
            var role=person.Admin?"ADMINISTRADOR":target?.Role=="ADMINISTRADOR"?"ADMINISTRADOR":"OPERADOR";var status=person.Rr?"RR":"ATIVO";
            if(target is null){using var insert=db.CreateCommand();insert.Transaction=tx;insert.CommandText="INSERT INTO Usuarios(Login,Documento,Nome,PostoGraduacao,Quartel,Funcao,Situacao,SenhaHash,Oculto,SenhaInicial) VALUES($l,$d,$n,$p,$q,$f,$s,$h,0,1)";insert.Parameters.AddWithValue("$l",person.Rgpm);insert.Parameters.AddWithValue("$d",person.Rgpm);insert.Parameters.AddWithValue("$n",person.Qra);insert.Parameters.AddWithValue("$p",person.Rank);insert.Parameters.AddWithValue("$q",person.Quarter);insert.Parameters.AddWithValue("$f",role);insert.Parameters.AddWithValue("$s",status);insert.Parameters.AddWithValue("$h",hash("15bpm"));insert.ExecuteNonQuery();added++;}
            else {using var update=db.CreateCommand();update.Transaction=tx;update.CommandText="UPDATE Usuarios SET Login=$l,Documento=$d,Nome=$n,PostoGraduacao=$p,Quartel=$q,Funcao=$f,Situacao=$s WHERE Id=$id";update.Parameters.AddWithValue("$l",person.Rgpm);update.Parameters.AddWithValue("$d",person.Rgpm);update.Parameters.AddWithValue("$n",person.Qra);update.Parameters.AddWithValue("$p",person.Rank);update.Parameters.AddWithValue("$q",person.Quarter);update.Parameters.AddWithValue("$f",role);update.Parameters.AddWithValue("$s",status);update.Parameters.AddWithValue("$id",target.Id);update.ExecuteNonQuery();updated++;if(target.Status!="ATIVO"&&status=="ATIVO")reactivated++;}
        }
        using(var version=db.CreateCommand()){version.Transaction=tx;version.CommandText="INSERT INTO VersoesBanco(Versao,AplicadaEm) VALUES(2,$date)";version.Parameters.AddWithValue("$date",DateTime.UtcNow.ToString("O"));version.ExecuteNonQuery();}
        using(var log=db.CreateCommand()){log.Transaction=tx;log.CommandText="INSERT INTO Logs(DataHora,Usuario,Funcao,Registro,Descricao,Permanente) VALUES($date,'admin','ADMINISTRADOR','EFETIVO',$text,0)";log.Parameters.AddWithValue("$date",DateTime.Now.ToString("O"));log.Parameters.AddWithValue("$text",$"Efetivo atualizado: {added} incluídos, {updated} atualizados e {reactivated} reativados. Backup: {Path.GetFileName(backupPath)}.");log.ExecuteNonQuery();}
        tx.Commit();
    }
    private sealed record Staff(string Rank,string Qra,string Rgpm,string Quarter,bool Rr,bool Admin);
    private sealed record Existing(long Id,string Login,string Document,string Name,string Quarter,string Role,string Status);
}
