using System.Diagnostics;
using System.Text;
using System.Linq;

// =================== Configurações ===================
const string CSV_URL = "https://www.gov.br/receitafederal/dados/municipios.csv";
const string OUT_DIR_NAME = "mun_bin_por_uf";

string baseDir = Directory.GetCurrentDirectory();
string localCsvPath = Path.Combine(baseDir, "municipios_local.csv");
string receitaCsvPath = Path.Combine(baseDir, "municipios_receita.csv");
string diffCsvPath = Path.Combine(baseDir, "municipios_diff.csv");
string outRoot = Path.Combine(baseDir, OUT_DIR_NAME);

using var httpClient = new HttpClient();

// ====================================================================
// FASE 1: Verificação e Mutação do Arquivo Base
// ====================================================================
Console.WriteLine("[FASE 1] Checando arquivo base local...");
if (!File.Exists(localCsvPath))
{
    Console.WriteLine("Arquivo local não encontrado. Realizando download inicial...");
    var initialBytes = await httpClient.GetByteArrayAsync(CSV_URL);
    await File.WriteAllBytesAsync(localCsvPath, initialBytes);
}

Console.WriteLine("Injetando alterações aleatórias no arquivo local (simulando inconsistência)...");
var linhasLocais = (await File.ReadAllLinesAsync(localCsvPath, Encoding.UTF8)).ToList();
if (linhasLocais.Count > 1)
{
    var rnd = new Random();
    // Altera 3 linhas aleatórias (ignorando o cabeçalho)
    for (int i = 0; i < 3; i++)
    {
        int index = rnd.Next(1, linhasLocais.Count);
        linhasLocais[index] = linhasLocais[index] + ";[MODIFICADO_PELO_SISTEMA]";
    }
    await File.WriteAllLinesAsync(localCsvPath, linhasLocais, Encoding.UTF8);
}

// ====================================================================
// FASE 2: Download da Fonte e Reconciliação (Diff)
// ====================================================================
Console.WriteLine("\n[FASE 2] Baixando 'fonte da verdade' da Receita Federal...");
var receitaBytes = await httpClient.GetByteArrayAsync(CSV_URL);
await File.WriteAllBytesAsync(receitaCsvPath, receitaBytes);

Console.WriteLine("Comparando base local com a Receita Federal...");
var linhasReceita = await File.ReadAllLinesAsync(receitaCsvPath, Encoding.UTF8);

// LINQ Except acha elementos que estão em uma lista mas não na outra
var diferencas = linhasLocais.Except(linhasReceita).Union(linhasReceita.Except(linhasLocais)).ToList();

if (diferencas.Any())
{
    Console.WriteLine($"Encontradas {diferencas.Count} diferenças! Salvando arquivo de diff...");
    await File.WriteAllLinesAsync(diffCsvPath, diferencas, Encoding.UTF8);
}
else
{
    Console.WriteLine("Nenhuma diferença encontrada.");
}

// ====================================================================
// FASE 3: Processamento e Salvamento em Formato Binário
// ====================================================================
Console.WriteLine("\n[FASE 3] Processando dados e salvando em formato binário por UF...");
Directory.CreateDirectory(outRoot);

int startIndex = (linhasReceita[0].IndexOf("IBGE", StringComparison.OrdinalIgnoreCase) >= 0) ? 1 : 0;
var municipios = new List<Municipio>(linhasReceita.Length - startIndex);

for (int i = startIndex; i < linhasReceita.Length; i++)
{
    var linha = (linhasReceita[i] ?? "").Trim();
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

var porUf = municipios
    .GroupBy(m => m.Uf, StringComparer.OrdinalIgnoreCase)
    .Where(g => !string.Equals(g.Key, "EX", StringComparison.OrdinalIgnoreCase))
    .ToList();

// Paralelismo mantido para I/O massivo
await Parallel.ForEachAsync(porUf, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (grupoUf, ct) =>
{
    string binPath = Path.Combine(outRoot, $"municipios_{grupoUf.Key}.bin");
    
    // Gravação Binária
    using (var fs = new FileStream(binPath, FileMode.Create, FileAccess.Write, FileShare.None))
    using (var bw = new BinaryWriter(fs, Encoding.UTF8))
    {
        // Escreve a quantidade de registros primeiro para facilitar a leitura futura
        bw.Write(grupoUf.Count());
        
        foreach (var m in grupoUf)
        {
            bw.Write(m.Tom ?? "");
            bw.Write(m.Ibge ?? "");
            bw.Write(m.NomeTom ?? "");
            bw.Write(m.NomeIbge ?? "");
            bw.Write(m.Uf ?? "");
        }
    }
});
Console.WriteLine($"Arquivos binários gerados com sucesso na pasta: {outRoot}");

// ====================================================================
// FASE 4: Motor de Busca Iterativo (Console)
// ====================================================================
Console.WriteLine("\n[FASE 4] Sistema de Busca Inicializado.");
Console.WriteLine("--------------------------------------------------");

while (true)
{
    Console.Write("\nDigite sua busca (UF, Código IBGE, ou parte do Nome) ou 'sair' para encerrar: ");
    string? input = Console.ReadLine()?.Trim();

    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input.Equals("sair", StringComparison.OrdinalIgnoreCase)) break;

    // Lógica de filtro usando LINQ
    var resultados = municipios.Where(m =>
        m.Uf.Equals(input, StringComparison.OrdinalIgnoreCase) ||
        m.Ibge.Equals(input, StringComparison.OrdinalIgnoreCase) ||
        m.Tom.Equals(input, StringComparison.OrdinalIgnoreCase) ||
        m.NomePreferido.Contains(input, StringComparison.OrdinalIgnoreCase)
    ).ToList();

    if (resultados.Any())
    {
        Console.WriteLine($"\n--- Encontrados {resultados.Count} resultados (Exibindo até 10) ---");
        foreach (var r in resultados.Take(10))
        {
            Console.WriteLine($"[{r.Uf}] Código IBGE: {r.Ibge} | Nome: {r.NomePreferido} | TOM: {r.Tom}");
        }
    }
    else
    {
        Console.WriteLine("\nNenhum município encontrado para a chave informada.");
    }
}

Console.WriteLine("Execução finalizada.");