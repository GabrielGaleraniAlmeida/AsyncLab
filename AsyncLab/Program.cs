using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Linq;

// =================== Configuração ===================
const int PBKDF2_ITERATIONS = 50_000;
const int HASH_BYTES = 32; 
const string CSV_URL = "https://www.gov.br/receitafederal/dados/municipios.csv";
const string OUT_DIR_NAME = "mun_hash_por_uf";

string FormatTempo(long ms)
{
    var ts = TimeSpan.FromMilliseconds(ms);
    return $"{ts.Minutes}m {ts.Seconds}s {ts.Milliseconds}ms";
}

var sw = Stopwatch.StartNew();

string baseDir = Directory.GetCurrentDirectory();
string tempCsvPath = Path.Combine(baseDir, "municipios.csv");
string outRoot = Path.Combine(baseDir, OUT_DIR_NAME);

Console.WriteLine("Baixando CSV de municípios (Receita Federal) ...");
// 1. I/O Assíncrono de Rede: Substituição do WebClient síncrono pelo HttpClient
using (var httpClient = new HttpClient())
{
    var csvBytes = await httpClient.GetByteArrayAsync(CSV_URL);
    await File.WriteAllBytesAsync(tempCsvPath, csvBytes);
}

Console.WriteLine("Lendo e parseando o CSV ...");
// 2. I/O Assíncrono de Disco
var linhas = await File.ReadAllLinesAsync(tempCsvPath, Encoding.UTF8);
if (linhas.Length == 0)
{
    Console.WriteLine("Arquivo CSV vazio.");
    return;
}

int startIndex = (linhas[0].IndexOf("IBGE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  linhas[0].IndexOf("UF", StringComparison.OrdinalIgnoreCase) >= 0) ? 1 : 0;

var municipios = new List<Municipio>(linhas.Length - startIndex);

for (int i = startIndex; i < linhas.Length; i++)
{
    var linha = (linhas[i] ?? "").Trim();
    if (string.IsNullOrWhiteSpace(linha)) continue;

    var parts = linha.Split(';');
    if (parts.Length < 5) continue;

    municipios.Add(new Municipio
    {
        Tom = Util.San(parts[0]),
        Ibge = Util.San(parts[1]),
        NomeTom = Util.San(parts[2]),
        NomeIbge = Util.San(parts[3]),
        Uf = Util.San(parts[4]).ToUpperInvariant()
    });
}

Console.WriteLine($"Registros lidos: {municipios.Count}");

// Grupo por UF
var porUf = municipios
    .GroupBy(m => m.Uf, StringComparer.OrdinalIgnoreCase)
    .Where(g => !string.Equals(g.Key, "EX", StringComparison.OrdinalIgnoreCase))
    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
    .ToList();

Directory.CreateDirectory(outRoot);
Console.WriteLine("Calculando hash por município e gerando arquivos por UF ...");

// 3. Paralelismo Assíncrono: Processa as UFs de forma paralela e não bloqueante
await Parallel.ForEachAsync(porUf, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (grupoUf, ct) =>
{
    var uf = grupoUf.Key;
    var listaUf = grupoUf.OrderBy(m => m.NomePreferido, StringComparison.OrdinalIgnoreCase).ToList();

    Console.WriteLine($"[START] Processando UF: {uf} ({listaUf.Count} municípios)");
    var swUf = Stopwatch.StartNew();
    
    string outPath = Path.Combine(outRoot, $"municipios_hash_{uf}.csv");
    string jsonPath = Path.Combine(outRoot, $"municipios_hash_{uf}.json");

    var listaJson = new List<object>();

    // 4. Concorrência CPU-Bound: Delega o cálculo do Hash (pesado) para múltiplas threads simultâneas
    var municipiosProcessados = listaUf.AsParallel().Select(m => 
    {
        string password = m.ToConcatenatedString();
        byte[] salt = Util.BuildSalt(m.Ibge);
        string hashHex = Util.DeriveHashHex(password, salt, PBKDF2_ITERATIONS, HASH_BYTES);
        
        return new 
        {
            Modelo = m,
            HashHex = hashHex,
            CsvLine = $"{m.Tom};{m.Ibge};{m.NomeTom};{m.NomeIbge};{m.Uf};{hashHex}"
        };
    }).ToList();

    // 5. I/O Assíncrono de Escrita (CSV e JSON)
    using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
    using (var swOut = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
    {
        await swOut.WriteLineAsync("TOM;IBGE;NomeTOM;NomeIBGE;UF;Hash");

        foreach (var item in municipiosProcessados)
        {
            await swOut.WriteLineAsync(item.CsvLine);
            listaJson.Add(new {
                item.Modelo.Tom,
                item.Modelo.Ibge,
                item.Modelo.NomeTom,
                item.Modelo.NomeIbge,
                item.Modelo.Uf,
                Hash = item.HashHex
            });
        }
    }

    using (var fsJson = new FileStream(jsonPath, FileMode.Create, FileAccess.Write, FileShare.None))
    {
        await JsonSerializer.SerializeAsync(fsJson, listaJson, new JsonSerializerOptions { WriteIndented = true }, ct);
    }

    swUf.Stop();
    Console.WriteLine($"[DONE] UF {uf} concluída. Tempo total UF: {FormatTempo(swUf.ElapsedMilliseconds)}");
});

sw.Stop();
Console.WriteLine("\n===== RESUMO =====");
Console.WriteLine($"UFs geradas: {porUf.Count}");
Console.WriteLine($"Pasta de saída: {outRoot}");
Console.WriteLine($"Tempo total: {FormatTempo(sw.ElapsedMilliseconds)} ({sw.Elapsed})");