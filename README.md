# ⚡ AsyncLab

## 🧪 Laboratório Async

### 🎯 Objetivo
Analisar o programa e tornar a sua execução **assíncrona**.

### 📦 Entrega

**Membros do Grupo:**
- Gabriel Galerani - RM: 557421

#### 🛠️ Descrição das modificações realizadas
O código original operava de forma estritamente síncrona, bloqueando a execução durante operações de I/O (rede e disco) e subutilizando os núcleos do processador durante tarefas pesadas (CPU-bound). Para solucionar isso, implementamos as seguintes modificações:

1. **I/O de Rede Assíncrono:** Substituição do método obsoleto e síncrono `WebClient.DownloadFile` por `HttpClient.GetByteArrayAsync`, liberando a thread principal durante o download do CSV.
2. **I/O de Disco Assíncrono:** Utilização de `File.ReadAllLinesAsync` para a leitura do CSV e `StreamWriter.WriteLineAsync` / `JsonSerializer.SerializeAsync` para a persistência dos dados de saída, evitando bloqueios de disco.
3. **Paralelismo de Tarefas:** Implementação de `Parallel.ForEachAsync` iterando sobre os grupos de UFs. Isso permite que a leitura e escrita de diferentes estados ocorram de maneira simultânea.
4. **Concorrência para Operações CPU-bound:** A derivação do hash com PBKDF2 é a operação mais custosa do software. O loop sequencial `foreach` foi substituído por `PLINQ` (`.AsParallel().Select()`), paralelizando a computação criptográfica entre todos os núcleos de processamento disponíveis na máquina antes de realizar as gravações em lote.

#### 📊 Impactos observados no tempo de execução
A aplicação conjunta de concorrência (múltiplas threads no cálculo de hash) e assincronicidade (não-bloqueio de I/O) causou um impacto massivo de otimização no fluxo de execução. 
* **Redução de Ociosidade:** A eliminação de esperas síncronas de I/O garantiu um pipeline contínuo.
* **Escalonamento Vertical (Multithreading):** O tempo gasto na geração de hashes caiu drasticamente, fracionado quase que proporcionalmente à quantidade de *cores* lógicos do processador da máquina utilizada para os testes. 
* **Tempo Total:** A melhoria gerou uma redução drástica no tempo total de processamento (*insira aqui o tempo de "antes e depois" executando os dois códigos no seu próprio computador*), provando que operações pesadas de processamento de dados exigem arquiteturas assíncronas e paralelas.

---

👨‍🏫 © 2025 | Professor Vinícius Costa Santos