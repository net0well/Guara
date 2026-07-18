# Calendários (feriados/exclusões) — Referência (Hangfire x Quartz.NET) para o Guará

> **Documento de referência de implementação.** Os trechos de código abaixo são extraídos dos repositórios originais (**Hangfire** — licença LGPL; **Quartz.NET** — licença Apache-2.0) e citados por arquivo apenas como **guia de comportamento**. Não copie literalmente para o Guará: entenda a semântica, os edge cases e os defaults, e reimplemente com a nossa arquitetura, nomenclatura em inglês (tipos) / português (API do usuário) e as invariantes do `docs/ARCHITECTURE.md` (zero terceiros no núcleo, zero reflection, AOT-safe).

---

## Panorama

Um **calendário** é um objeto que **restringe** quando um agendamento pode disparar. Ele não gera horários de disparo — ele apenas responde à pergunta "este instante está *incluído* (permitido)?". O padrão dominante é **excluir** blocos de tempo: feriados, fins de semana, janelas fora do horário comercial, etc. O cálculo do próximo disparo (cron/intervalo) roda normalmente e, **para cada candidato**, consulta o calendário; se o candidato cair numa data/janela excluída, ele é **pulado** e o cálculo avança para o próximo.

| Ferramenta | Tem calendários? | Observação |
|---|---|---|
| **Hangfire** | **NÃO** | Hangfire **não possui nenhum conceito de calendário/feriado/exclusão**. As únicas ocorrências de "calendar" no repositório estão em `moment-with-locales.min.js`, `bootstrap.min.css` e arquivos `packages.lock.json` — nada funcional. Recorrentes no Hangfire (`RecurringJob`) só têm cron + timezone + misfire; para pular feriados o usuário teria que codar a checagem dentro do próprio job. **Não há referência a extrair do Hangfire nesta área.** |
| **Quartz.NET** | **SIM** | Modelo maduro: interface `ICalendar`, classe-base `BaseCalendar`, 6 implementações concretas (`HolidayCalendar`, `CronCalendar`, `DailyCalendar`, `WeeklyCalendar`, `MonthlyCalendar`, `AnnualCalendar`), encadeamento de calendários (base calendars), consulta no cálculo do disparo via `IsTimeIncluded`/`GetNextIncludedTimeUtc`, registro no scheduler via `AddCalendar(..., updateTriggers)` e persistência (RAM e ADO/BLOB). É **a** referência para o Guará. |

Como este documento trata de uma funcionalidade que **só existe no Quartz**, a seção do Hangfire é curta e as recomendações para o Guará derivam inteiramente do Quartz + spec 038.

---

## Hangfire — visão geral

**Hangfire não implementa calendários.** Confirmado por busca no repositório inteiro (`C:/Users/Welligton Neto/Desktop/Estudos/Hangfire`): não há `ICalendar`, `Calendar`, `Holiday`, `Exclusion`, `IsTimeIncluded` ou equivalente no código-fonte de `Hangfire.Core`. O agendamento recorrente do Hangfire (`RecurringJob`/`RecurringJobEntity`) resolve o próximo disparo apenas por expressão cron (parser `Hangfire.Cronos`) + `TimeZoneInfo` + política de misfire. Não há gancho para "pular feriados".

**Consequência para o Guará:** nesta área não há trecho de código do Hangfire para espelhar. O modelo canônico é o do Quartz, e o Guará já decidiu adotá-lo (spec 038). Onde este documento diz "adote X do Quartz", o "X" não tem contraparte no Hangfire.

*(Se um dia quiséssemos uma paridade mínima com Hangfire, o comportamento seria "sem calendários" — exatamente o que **não** queremos; o Guará se diferencia justamente por ter calendários de primeira classe estilo Quartz.)*

---

## Quartz.NET — visão geral / classes-chave / trechos / fluxo

### Classes-chave (arquivo → responsabilidade)

| Arquivo | Responsabilidade |
|---|---|
| `src/Quartz/ICalendar.cs` | Interface do calendário: `Description`, `CalendarBase` (encadeamento), `IsTimeIncluded(dto)`, `GetNextIncludedTimeUtc(dto)`, `Clone()`. Convenção central: calendários **excluem** blocos; a maioria "inclui tudo por padrão". |
| `src/Quartz/Impl/Calendar/BaseCalendar.cs` | Classe-base opcional. Implementa: encadeamento (base calendar stacking), `TimeZone` (default `TimeZoneInfo.Local`), `Description`, `Clone`/`CloneFields`, `Equals`/`GetHashCode`, e o contrato `ISerializable` versionado. `IsTimeIncluded`/`GetNextIncludedTimeUtc` só delegam ao calendário-base. |
| `src/Quartz/Impl/Calendar/HolidayCalendar.cs` | Exclui **dias inteiros específicos** (feriados). Guarda um `SortedSet<DateTime>` (só ano/mês/dia importam). Precisão de dia inteiro. **Considera o ano** (25/12/2026 ≠ 25/12/2027). |
| `src/Quartz/Impl/Calendar/AnnualCalendar.cs` | Exclui **dias que se repetem todo ano** (ex.: todo 25/12). Guarda mês/dia com ano fixo `2000`. |
| `src/Quartz/Impl/Calendar/WeeklyCalendar.cs` | Exclui **dias da semana** (`bool[7]`). Default: exclui sábado e domingo. Otimização `excludeAll`. |
| `src/Quartz/Impl/Calendar/MonthlyCalendar.cs` | Exclui **dias do mês** (1–31, `bool[31]`). Otimização `excludeAll`. |
| `src/Quartz/Impl/Calendar/CronCalendar.cs` | Exclui **os instantes que casam com uma expressão cron** (janelas por cron). Precisão de milissegundos. |
| `src/Quartz/Impl/Calendar/DailyCalendar.cs` | Exclui **uma faixa de horário por dia** (ex.: 08:00–17:00). Não cruza a meia-noite. `InvertTimeRange` inverte (exclui *fora* da faixa). |
| `src/Quartz/Core/QuartzScheduler.cs` | `AddCalendar(name, calendar, replace, updateTriggers)` / `DeleteCalendar(name)` — API do scheduler; delega ao `IJobStore`. |
| `src/Quartz/Simpl/RAMJobStore.cs` | `StoreCalendar`/`RemoveCalendar`/`RetrieveCalendar` em memória. Faz o **recálculo dos triggers** quando `updateTriggers=true` e bloqueia remoção de calendário em uso. Clona na entrada e na saída. |
| `src/Quartz/Impl/AdoJobStore/JobStoreSupport.cs` | Mesmo contrato persistido em banco: calendário serializado como **BLOB**, `updateTriggers` re-armazena cada trigger afetado. |
| `src/Quartz/Impl/Triggers/CronTriggerImpl.cs` (e `SimpleTriggerImpl`, `DailyTimeIntervalTriggerImpl`, etc.) | Onde o calendário é **consultado** no cálculo do disparo: `ComputeFirstFireTimeUtc`, `Triggered`, `UpdateWithNewCalendar`. |
| `src/Quartz/Configuration/CalendarConfiguration.cs` | DTO interno usado pelo registro fluente `q.AddCalendar<T>(name, replace, updateTriggers, ...)` na DI. |

### O contrato: `ICalendar`

O arquivo `ICalendar.cs` documenta a filosofia inteira em comentário — vale ler:

`src/Quartz/ICalendar.cs`
```csharp
/// An interface to be implemented by objects that define spaces of time during
/// which an associated ITrigger may (not) fire. Calendars do not define actual
/// fire times, but rather are used to limit a ITrigger from firing on its normal
/// schedule if necessary. Most Calendars include all times by default and allow
/// the user to specify times to exclude.
public interface ICalendar
{
    string? Description { get; set; }
    ICalendar? CalendarBase { set; get; }              // encadeamento (stacking)
    bool IsTimeIncluded(DateTimeOffset timeUtc);        // este instante é permitido?
    DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc); // próximo permitido
    ICalendar Clone();
}
```

Dois pontos de design que o Guará deve absorver:
1. **Implementações DEVEM ser cloneáveis e serializáveis** (comentário no XML doc). O scheduler clona na entrada/saída para isolar mutações — ver `RAMJobStore` adiante.
2. **`CalendarBase`** permite **empilhar** calendários: um `HolidayCalendar` cujo base é um `WeeklyCalendar` exclui *feriados E fins de semana* numa única consulta.

### A base: `BaseCalendar` (encadeamento + timezone + serialização)

`src/Quartz/Impl/Calendar/BaseCalendar.cs`
```csharp
// Encadeamento: cada IsTimeIncluded consulta o calendário-base PRIMEIRO.
public virtual bool IsTimeIncluded(DateTimeOffset timeStampUtc)
{
    if (timeStampUtc == DateTimeOffset.MinValue)
        Throw.ArgumentException("timeStampUtc must be greater 0");

    if (CalendarBase is not null)
    {
        if (!CalendarBase.IsTimeIncluded(timeStampUtc))
            return false;   // se o base já exclui, encerra — curto-circuito
    }
    return true;            // BaseCalendar puro inclui tudo
}

public virtual DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
{
    if (timeUtc == DateTimeOffset.MinValue)
        Throw.ArgumentException("timeStamp must be greater DateTimeOffset.MinValue");

    if (CalendarBase is not null)
        return CalendarBase.GetNextIncludedTimeUtc(timeUtc);
    return timeUtc;
}
```

**TimeZone**: default é `TimeZoneInfo.Local` (lazy). Calendários de precisão de dia (`Holiday`, `Weekly`, `Monthly`, `Annual`) convertem o instante UTC para esse fuso **antes** de decidir a data/dia da semana. Isto importa: "exclua 25/12" depende do fuso em que "25/12" é avaliado.

`src/Quartz/Impl/Calendar/BaseCalendar.cs`
```csharp
public virtual TimeZoneInfo TimeZone
{
    get
    {
        if (timeZone is null)
            timeZone = TimeZoneInfo.Local;   // ⚠ default depende da máquina
        return timeZone;
    }
    set => timeZone = value;
}
```

**Clone**: `CloneFields` propaga `Description`, `TimeZone` e **clona o calendário-base recursivamente** — cópia profunda da cadeia.

`src/Quartz/Impl/Calendar/BaseCalendar.cs`
```csharp
protected BaseCalendar CloneFields(BaseCalendar clone)
{
    clone.Description = Description;
    clone.TimeZone = TimeZone;
    clone.CalendarBase = CalendarBase?.Clone();  // deep clone da cadeia
    return clone;
}
```

**Serialização versionada** (`ISerializable`): o `BaseCalendar` grava um `baseCalendarVersion` e, para timezones, serializa o **Id** (`timeZoneId`) — nunca o objeto `TimeZoneInfo` (que não é portável Windows↔IANA). O construtor de deserialização faz `switch (version)` e lança em versões desconhecidas. Cada subclasse repete o padrão com seu próprio `"version"`.

`src/Quartz/Impl/Calendar/BaseCalendar.cs`
```csharp
[System.Security.SecurityCritical]
public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
{
    info.AddValue("baseCalendarVersion", 1);
    info.AddValue("baseCalendar", CalendarBase);
    info.AddValue("description", Description);
    info.AddValue("timeZoneId", timeZone?.Id);   // grava só o Id, não o objeto
}
```

> **Nota para o Guará:** o Quartz usa `ISerializable` (BinaryFormatter/DCS) porque é antigo e precisa de compat binária no banco. O Guará **não deve** usar `ISerializable` (ADR-0009, AOT-safe) — persistir calendários como **JSON via `System.Text.Json` com source generator**. Mas o **conceito de versionamento do payload** (um campo `version`/`schema` no JSON) é uma boa ideia a manter para evolução de esquema.

### `HolidayCalendar` — feriados (datas absolutas, dia inteiro)

Guarda um `SortedSet<DateTime>` normalizado para `.Date`. `IsTimeIncluded` converte para o fuso e checa `Contains`.

`src/Quartz/Impl/Calendar/HolidayCalendar.cs`
```csharp
private SortedSet<DateTime> dates = new SortedSet<DateTime>();

public override bool IsTimeIncluded(DateTimeOffset timeStampUtc)
{
    if (!base.IsTimeIncluded(timeStampUtc))   // encadeamento primeiro
        return false;
    return IsTimeIncludedThisCalendar(timeStampUtc);
}

private bool IsTimeIncludedThisCalendar(DateTimeOffset timeStampUtc)
{
    timeStampUtc = TimeZoneUtil.ConvertTime(timeStampUtc, TimeZone); // aplica fuso
    var lookFor = timeStampUtc.Date;                                // zera a hora
    return !dates.Contains(lookFor);
}

public void AddExcludedDate(DateTime excludedDateUtc) => dates.Add(excludedDateUtc.Date);
public void RemoveExcludedDate(DateTime dateToRemoveUtc) => dates.Remove(dateToRemoveUtc.Date);
```

`GetNextIncludedTimeUtc` **anda dia a dia** até achar um dia não-excluído (respeitando também o base calendar em cada passo):

`src/Quartz/Impl/Calendar/HolidayCalendar.cs`
```csharp
public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
{
    DateTimeOffset baseTime = base.GetNextIncludedTimeUtc(timeUtc);
    if (timeUtc != DateTimeOffset.MinValue && baseTime > timeUtc)
        timeUtc = baseTime;

    timeUtc = TimeZoneUtil.ConvertTime(timeUtc, TimeZone);
    DateTimeOffset day = new DateTimeOffset(timeUtc.Date, timeUtc.Offset); // 00:00

    while (!IsTimeIncludedThisCalendar(day) || !base.IsTimeIncluded(timeUtc))
    {
        day = day.AddDays(1);
        timeUtc = timeUtc.AddDays(1);
        if (day < timeUtc) timeUtc = day; // pega o menor dos dois — combina as cadeias
    }
    return timeUtc;
}
```

**Detalhe importante — considera o ano:** o XML-doc diz explicitamente "para excluir 4 de julho pelos próximos 10 anos, adicione 10 entradas". Use `AnnualCalendar` se quer a recorrência anual sem repetir entradas.

**Serialização:** só aceita `version = 2` (um `DateTime[]`); versões antigas (0/1) lançam `NotSupportedException` orientando a re-serializar com Quartz 2.x. Lição: **migração de esquema é uma dor de cabeça real** — planeje-a desde o início.

### `AnnualCalendar` — feriados que repetem todo ano (mês/dia)

Guarda datas com **ano fixo `2000`**; compara só mês+dia.

`src/Quartz/Impl/Calendar/AnnualCalendar.cs`
```csharp
private const int FixedYear = 2000;

public void SetDayExcluded(DateTimeOffset day, bool exclude)
{
    DateTime d = new (FixedYear, day.Month, day.Day, 0, 0, 0);
    if (exclude) { if (!IsDateTimeExcluded(day, false)) excludeDays.Add(d); }
    else         { if (IsDateTimeExcluded(day, false))  excludeDays.Remove(d); }
}

private bool IsDateTimeExcluded(DateTimeOffset day, bool checkBaseCalendar)
{
    if (checkBaseCalendar && !base.IsTimeIncluded(day)) return true;

    int dmonth = day.Month, dday = day.Day;
    foreach (DateTime cl in excludeDays)   // set ordenado (mês, depois dia)
    {
        if (dmonth < cl.Month) return false; // otimização: passou do mês → não achou
        if (dday   != cl.Day)  continue;
        if (dmonth != cl.Month) continue;
        return true;
    }
    return false;
}
```

> **Armadilha sutil (leia com atenção):** a ordem dos testes dentro do `foreach` é `dday != cl.Day` **antes** de `dmonth != cl.Month`. Como o `SortedSet<DateTime>` está ordenado por data completa (ano fixo → efetivamente mês, depois dia), o early-return `dmonth < cl.Month` depende dessa ordenação para funcionar. É um código frágil que já teve bugs históricos no Quartz. **No Guará, prefira uma estrutura explícita** (ex.: `HashSet<(int Month, int Day)>`) e uma checagem O(1) direta, sem depender de ordenação implícita.

### `WeeklyCalendar` — dias da semana (default: fim de semana)

`bool[7]` indexado por `DayOfWeek` (domingo = 0). Default exclui sábado+domingo. Otimização `excludeAll`: se **todos** os dias forem excluídos, `IsTimeIncluded` retorna `false` direto e `GetNextIncludedTimeUtc` retorna `DateTimeOffset.MinValue` (sinal de "nunca").

`src/Quartz/Impl/Calendar/WeeklyCalendar.cs`
```csharp
private void Init()
{
    excludeDays[(int) DayOfWeek.Sunday]   = true;
    excludeDays[(int) DayOfWeek.Saturday] = true;
    excludeAll = AreAllDaysExcluded();
}

public override bool IsTimeIncluded(DateTimeOffset timeUtc)
{
    if (excludeAll) return false;
    if (!base.IsTimeIncluded(timeUtc)) return false;
    timeUtc = TimeZoneUtil.ConvertTime(timeUtc, TimeZone); // fuso decide o dia da semana
    return !IsDayExcluded(timeUtc.DayOfWeek);
}

public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
{
    if (excludeAll) return DateTimeOffset.MinValue;    // "nunca"
    // ... aplica base + fuso, zera a hora, anda dia a dia até dia não-excluído
    DateTimeOffset d = new DateTimeOffset(timeUtc.Date, timeUtc.Offset);
    if (!IsDayExcluded(d.DayOfWeek)) return d;
    while (IsDayExcluded(d.DayOfWeek)) d = d.AddDays(1);
    return d;
}
```

### `MonthlyCalendar` — dias do mês (1–31)

`bool[31]`, index `day-1`. Mesma otimização `excludeAll`. `IsDayExcluded` valida faixa 1–31.

`src/Quartz/Impl/Calendar/MonthlyCalendar.cs`
```csharp
public bool IsDayExcluded(int day)
{
    if (day < 1 || day > MaxDaysInMonth)  // MaxDaysInMonth = 31
        Throw.ArgumentException($"The day parameter must be in the range of 1 to {MaxDaysInMonth}");
    return excludeDays[day - 1];
}
```

> **Armadilha sutil:** o comentário do próprio Quartz no `Equals` reconhece que `MonthlyCalendar` "não sabe nada sobre o mês específico" — excluir o dia 31 num mês que não tem dia 31 simplesmente nunca casa. E o `Equals` compara o array inteiro, então dois calendários que diferem só no dia 31 são "diferentes" mesmo em fevereiro. Detalhe de borda a documentar se o Guará oferecer `ExcluirDiaDoMes`.

### `CronCalendar` — janelas por expressão cron (precisão de ms)

Exclui **os instantes que casam com a cron**. Ex.: `"* * 0-7,18-23 ? * *"` exclui tudo fora do horário comercial. Precisão de **milissegundos** (ao contrário dos calendários de dia inteiro).

`src/Quartz/Impl/Calendar/CronCalendar.cs`
```csharp
public override bool IsTimeIncluded(DateTimeOffset timeUtc)
{
    if (CalendarBase is not null && CalendarBase.IsTimeIncluded(timeUtc) == false)
        return false;
    return !cronExpression.IsSatisfiedBy(timeUtc);   // casou a cron ⇒ EXCLUÍDO
}

public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
{
    DateTimeOffset nextIncludedTime = timeUtc.AddMilliseconds(1);
    while (!IsTimeIncluded(nextIncludedTime))
    {
        if (cronExpression.IsSatisfiedBy(nextIncludedTime))
        {
            // dentro de uma janela excluída: pula direto para o fim dela
            nextIncludedTime = cronExpression.GetNextValidTimeAfter(nextIncludedTime)!.Value;
        }
        else if (CalendarBase is not null && !CalendarBase.IsTimeIncluded(nextIncludedTime))
        {
            nextIncludedTime = CalendarBase.GetNextIncludedTimeUtc(nextIncludedTime);
        }
        else
        {
            nextIncludedTime = nextIncludedTime.AddMilliseconds(1); // fallback lento
        }
    }
    return nextIncludedTime;
}
```

**Detalhe de performance:** o algoritmo tenta pular a janela inteira via `GetNextValidTimeAfter` em vez de avançar 1ms de cada vez (que é o fallback). O `TimeZone` do `CronCalendar` delega para o `TimeZone` da própria `CronExpression` — não tem campo próprio.

### `DailyCalendar` — faixa de horário diária (não cruza meia-noite)

Uma única faixa `HH:MM[:SS[:mmm]]` excluída **todo dia**. `InvertTimeRange=false` (default) exclui *dentro* da faixa; `true` exclui *fora* dela. **Não pode cruzar a meia-noite** — o construtor valida `start < end` e lança se não for.

`src/Quartz/Impl/Calendar/DailyCalendar.cs`
```csharp
public override bool IsTimeIncluded(DateTimeOffset timeUtc)
{
    if (CalendarBase is not null && CalendarBase.IsTimeIncluded(timeUtc) == false)
        return false;

    timeUtc = TimeZoneUtil.ConvertTime(timeUtc, TimeZone);
    DateTimeOffset startOfDay = GetStartOfDay(timeUtc);
    DateTimeOffset endOfDay   = GetEndOfDay(timeUtc);
    DateTimeOffset rangeStart = GetTimeRangeStartingTimeUtc(timeUtc);
    DateTimeOffset rangeEnd   = GetTimeRangeEndingTimeUtc(timeUtc);

    if (!InvertTimeRange)
    {
        // incluído se ANTES do início OU DEPOIS do fim da faixa (ou seja, fora da faixa)
        if (timeUtc >= startOfDay && timeUtc < rangeStart ||
            timeUtc >  rangeEnd   && timeUtc <= endOfDay) return true;
        return false;
    }
    // invertido: incluído somente DENTRO da faixa
    if (timeUtc >= rangeStart && timeUtc <= rangeEnd) return true;
    return false;
}
```

Validação da faixa no `SetTimeRange` (não cruza meia-noite):

`src/Quartz/Impl/Calendar/DailyCalendar.cs`
```csharp
if (!(startCal < endCal))
    Throw.ArgumentException($"{InvalidTimeRange}{...}"); // faixa inválida se start >= end
```

> **Contraste com o Guará (spec 038):** o Guará quer **janelas que cruzam a meia-noite** no `.EntreHorarios(inicio, fim)` do `.ACada(...)` — "`início > fim` significa janela que cruza a meia-noite (ex.: 22:00–06:00)". O `DailyCalendar` do Quartz **proíbe** isso (só faixa dentro do mesmo dia). Ou seja, o mecanismo de janela do `ACada` do Guará é mais expressivo que o `DailyCalendar` — não o modele em cima dele; trate a janela do intervalo separadamente do sistema de calendários.

### `DailyCalendar` usa `TimeProvider`

Detalhe moderno e AOT-friendly já presente no Quartz recente: `DailyCalendar` injeta `TimeProvider` (default `TimeProvider.System`) para obter "agora" ao normalizar a faixa. **O Guará deve fazer isto em TODOS os calendários e no scheduler** — testabilidade com `FakeTimeProvider`.

`src/Quartz/Impl/Calendar/DailyCalendar.cs`
```csharp
public DailyCalendar(string rangeStartingTime, string rangeEndingTime, TimeProvider? timeProvider = null)
    : this(timeProvider ?? TimeProvider.System)
{
    SetTimeRange(rangeStartingTime, rangeEndingTime);
}
```

### Fluxo passo a passo — como um calendário entra no cálculo do disparo

Este é o ponto mais importante para o Guará. Quem consulta o calendário **não é o calendário**, é o **trigger** (equivalente do nosso recorrente), no momento de calcular o próximo disparo. E — detalhe crucial — o trigger usa **`IsTimeIncluded` num laço com `GetFireTimeAfter`**, e **não** `GetNextIncludedTimeUtc`:

`src/Quartz/Impl/Triggers/CronTriggerImpl.cs` — primeiro disparo:
```csharp
public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar? cal)
{
    var now = TimeProvider.GetUtcNow();
    if (EndTimeUtc.HasValue && EndTimeUtc.Value < now) return null; // vigência expirada

    nextFireTimeUtc = GetFireTimeAfter(startTimeUtc.AddSeconds(-1));
    if (nextFireTimeUtc.HasValue && nextFireTimeUtc.Value < now)
        nextFireTimeUtc = GetFireTimeAfter(now);   // não dispara "no passado"

    // >>> aqui o calendário entra: pula candidatos excluídos <<<
    while (nextFireTimeUtc.HasValue && cal is not null && !cal.IsTimeIncluded(nextFireTimeUtc.Value))
        nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

    return nextFireTimeUtc;
}
```

`src/Quartz/Impl/Triggers/CronTriggerImpl.cs` — a cada disparo (avança para o próximo):
```csharp
public override void Triggered(ICalendar? cal)
{
    previousFireTimeUtc = nextFireTimeUtc;
    nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

    while (nextFireTimeUtc.HasValue && cal is not null && !cal.IsTimeIncluded(nextFireTimeUtc.Value))
        nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
}
```

**Padrão canônico (grave isto):**
```
proximo = agenda.ProximoApos(candidato)
enquanto (proximo != null && calendario != null && !calendario.IsTimeIncluded(proximo))
    proximo = agenda.ProximoApos(proximo)
```

O `GetNextIncludedTimeUtc` existe (e cada calendário o implementa bem), mas o loop de escalonamento real prefere `IsTimeIncluded` + recomputar a agenda — porque avançar só o calendário poderia cair num instante que a **agenda** não gera. É a interseção "agenda ∩ calendário".

**Guarda anti-loop-infinito:** o Quartz tem `TriggerConstants.YearToGiveUpSchedulingAt` — se o próximo disparo ultrapassar esse ano, desiste (`nextFireTimeUtc = null`). Visível no `UpdateWithNewCalendar`:

`src/Quartz/Impl/Triggers/CronTriggerImpl.cs`
```csharp
public override void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold)
{
    nextFireTimeUtc = GetFireTimeAfter(previousFireTimeUtc);
    if (!nextFireTimeUtc.HasValue || calendar is null) return;

    DateTimeOffset now = TimeProvider.GetUtcNow();
    while (nextFireTimeUtc.HasValue && !calendar.IsTimeIncluded(nextFireTimeUtc.Value))
    {
        nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
        if (!nextFireTimeUtc.HasValue) break;

        // anti-loop-infinito: se um calendário exclui tudo, desiste em vez de travar
        if (nextFireTimeUtc.Value.Year > TriggerConstants.YearToGiveUpSchedulingAt)
            nextFireTimeUtc = null;

        // respeita misfire ao "pular" o passado
        if (nextFireTimeUtc.HasValue && nextFireTimeUtc.Value < now)
        {
            TimeSpan diff = now - nextFireTimeUtc.Value;
            if (diff >= misfireThreshold)
                nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
        }
    }
}
```

### `AddCalendar` / `updateTriggers` — registro e recálculo automático

O scheduler só repassa ao job store:

`src/Quartz/Core/QuartzScheduler.cs`
```csharp
public ValueTask AddCalendar(string name, ICalendar calendar, bool replace,
                             bool updateTriggers, CancellationToken cancellationToken = default)
{
    ValidateState();
    return resources.JobStore.StoreCalendar(name, calendar, replace, updateTriggers, cancellationToken);
}
```

O trabalho de verdade está no store. **Clona na entrada**, respeita `replace`, e quando `updateTriggers=true` **E** já existia um calendário com aquele nome (`obj is not null`), recalcula o próximo disparo de **cada trigger que referencia esse calendário** — reinserindo na fila ordenada de tempos (`timeTriggers`):

`src/Quartz/Simpl/RAMJobStore.cs`
```csharp
public virtual async ValueTask StoreCalendar(string name, ICalendar calendar,
    bool replaceExisting, bool updateTriggers, CancellationToken cancellationToken = default)
{
    calendar = calendar.Clone();                       // defensivo — isola do chamador
    await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        calendarsByName.TryGetValue(name, out var obj);
        if (obj is not null && !replaceExisting)
            Throw.ObjectAlreadyExistsException($"Calendar with name '{name}' already exists.");
        if (obj is not null)
            calendarsByName.TryRemove(name, out _);
        calendarsByName[name] = calendar;

        if (obj is not null && updateTriggers)         // só recalcula se SUBSTITUIU um existente
        {
            foreach (TriggerWrapper tw in GetTriggerWrappersForCalendarNoLock(name))
            {
                bool removed = timeTriggers.Remove(tw);            // tira da fila ordenada
                tw.Trigger.UpdateWithNewCalendar(calendar, MisfireThreshold); // recalcula NextRun
                if (removed) timeTriggers.Add(tw);                 // reinsere reordenado
            }
        }
    }
    finally { lockObject.Release(); }
}
```

Na versão persistida (banco), o mesmo, com o calendário indo para BLOB e cada trigger afetado sendo re-armazenado:

`src/Quartz/Impl/AdoJobStore/JobStoreSupport.cs`
```csharp
if (existingCal)
{
    if (await Delegate.UpdateCalendar(conn, calName, calendar, ct) < 1)
        Throw.JobPersistenceException("Couldn't store calendar.  Update failed.");

    if (updateTriggers)
    {
        var triggers = await Delegate.SelectTriggersForCalendar(conn, calName, ct);
        foreach (IOperableTrigger trigger in triggers)
        {
            trigger.UpdateWithNewCalendar(calendar, MisfireThreshold);
            string state = await Delegate.SelectTriggerState(conn, trigger.Key, ct);
            if (string.Equals(state, StateDeleted, StringComparison.Ordinal)) continue;
            await StoreTrigger(conn, trigger, null, true, state, true, false, ct);
        }
    }
}
else
{
    if (await Delegate.InsertCalendar(conn, calName, calendar, ct) < 1)
        Throw.JobPersistenceException("Couldn't store calendar.  Insert failed.");
}
```

### `RemoveCalendar` — bloqueia remoção de calendário em uso

Se algum trigger referencia o calendário, a remoção **falha** (não deixa órfãos):

`src/Quartz/Simpl/RAMJobStore.cs`
```csharp
private bool RemoveCalendarNoLock(string name)
{
    int numRefs = 0;
    foreach (TriggerWrapper triggerWrapper in triggersByKey.Values)
    {
        IOperableTrigger trigg = triggerWrapper.Trigger;
        if (trigg.CalendarName is not null && trigg.CalendarName == name)
            numRefs++;
    }
    if (numRefs > 0)
        Throw.JobPersistenceException("Calender cannot be removed if it referenced by a Trigger!");
    return calendarsByName.TryRemove(name, out _);
}
```

### `RetrieveCalendar` — clona na saída também

`src/Quartz/Simpl/RAMJobStore.cs`
```csharp
public virtual async ValueTask<ICalendar?> RetrieveCalendar(string name, CancellationToken ct = default)
{
    await lockObject.WaitAsync(ct).ConfigureAwait(false);
    try
    {
        calendarsByName.TryGetValue(name, out var calendar);
        return calendar?.Clone();   // nunca entrega a instância interna
    }
    finally { lockObject.Release(); }
}
```

> **Regra de ouro (isolamento):** clonar na entrada (`StoreCalendar`) **e** na saída (`RetrieveCalendar`) garante que ninguém mute o calendário no store por baixo dos panos. O Guará deve seguir: os objetos persistidos são imutáveis do ponto de vista de quem os recebe.

### Registro fluente na DI

O Quartz também expõe registro por DI (`q.AddCalendar<T>(name, replace, updateTriggers, configure)`), materializado neste DTO interno:

`src/Quartz/Configuration/CalendarConfiguration.cs`
```csharp
internal sealed class CalendarConfiguration
{
    public CalendarConfiguration(string name, ICalendar calendar, bool replace,
                                 bool updateTriggers, string? optionsName = null) { ... }
    public string Name { get; }
    public ICalendar Calendar { get; }
    public bool Replace { get; }
    public bool UpdateTriggers { get; }
    public string OptionsName { get; }
}
```

---

## Comparação lado a lado

| Aspecto | Hangfire | Quartz.NET | Recomendação Guará |
|---|---|---|---|
| Conceito de calendário | **Inexistente** | `ICalendar` + `BaseCalendar` + 6 impls | Adotar o modelo Quartz (spec 038) |
| Tipos prontos | — | Holiday, Annual, Weekly, Monthly, Cron, Daily | Começar com Data, Intervalo, DiasDaSemana, Cron (spec 038); mensal/lunar depois (DD-2) |
| Semântica | — | Calendário **exclui**; "inclui tudo por default" | Igual: `ICalendarBuilder.Excluir*` |
| Encadeamento | — | `CalendarBase` (stacking, deep clone) | Adotar: um calendário pode ter base; útil para "feriados + fins de semana" |
| Consulta no disparo | — | `while(!cal.IsTimeIncluded(x)) x = agenda.Next(x)` no trigger | Adotar exatamente esse laço interseção agenda∩calendário |
| Método de "próximo incluído" | — | `GetNextIncludedTimeUtc` (existe, mas o loop usa `IsTimeIncluded`) | Implementar `IsTimeIncluded` como primário; `GetNextIncludedTimeUtc` opcional/otimização |
| Recálculo ao editar calendário | — | `updateTriggers=true` → `UpdateWithNewCalendar` em todos os triggers do calendário | Já é requisito (AC-6): recálculo **automático** e implícito |
| Remoção de calendário em uso | — | **Bloqueada** (lança exceção) | Já é requisito (spec 038 Edge Cases + semantics.md) |
| Fuso horário | — | `TimeZone` por calendário (default `Local`) | Fuso vem do recorrente (`NoFusoHorario`), nativo `TimeZoneInfo` (sem TimeZoneConverter) |
| Guarda anti-loop | — | `YearToGiveUpSchedulingAt` | Adotar limite; se calendário exclui tudo → "sem próxima ocorrência" + aviso (spec 038) |
| Isolamento | — | Clona na entrada e na saída | Adotar (objetos imutáveis para quem recebe) |
| Persistência | — | `ISerializable`/BLOB (RAM e ADO), versionada | JSON via `System.Text.Json` + source gen (AOT); manter campo `version` |
| Precisão | — | Dia inteiro (Holiday/Annual/Weekly/Monthly) ou ms (Cron/Daily) | Data/DiasSemana = dia; Cron = instante |
| `TimeProvider` | — | Presente no `DailyCalendar` (recente) | Usar em **todos** os calendários + scheduler (testes com FakeTimeProvider) |

---

## O que o Guará já faz / deve adotar / pode melhorar

### Já previsto nas specs (bom — mantenha)

- **`ICalendarBuilder`** com `ExcluirData(DateOnly)`, `ExcluirIntervalo(DateOnly, DateOnly)`, `ExcluirDiasDaSemana(params DayOfWeek[])`, `ExcluirCron(expr)` (spec 038, Domain Model). Cobre `HolidayCalendar` (via `ExcluirData`/`ExcluirIntervalo`), `WeeklyCalendar` (`ExcluirDiasDaSemana`) e `CronCalendar` (`ExcluirCron`) do Quartz de uma vez.
- **Estrutura `Calendars`** no storage, gerida por `IRecurringStorage` (spec 004, linhas 51–52). Alinha com o `calendarsByName`/tabela `QRTZ_CALENDARS` do Quartz.
- **Recálculo automático (`updateTriggers` implícito)** ao alterar o calendário — spec 038 AC-6 e semantics.md linha 58: "calendário editado (código **ou** dashboard) recalcula todos os recorrentes que o usam". Espelha `StoreCalendar(updateTriggers:true)` → `UpdateWithNewCalendar`.
- **Remoção de calendário em uso bloqueada** — spec 038 Edge Cases + semantics.md linha 72. Espelha `RemoveCalendarNoLock`.
- **Calendário que exclui todas as ocorrências futuras** → recorrente "sem próxima ocorrência" + aviso (spec 038 Edge Cases). Espelha a guarda `YearToGiveUpSchedulingAt`.
- **Semântica "pula para a próxima válida"** (spec 038 AC-5; semantics.md linha 58). Espelha o laço `IsTimeIncluded`.
- **Gestão pelo dashboard** (spec 032) pelo **mesmo caminho de persistência e recálculo** (spec 038). Igual ao Quartz, onde `AddCalendar` é único ponto de entrada.
- **Fuso nativo** (`TimeZoneInfo.TryConvertIanaIdToWindowsId`), sem terceiros (spec 038). Melhor que o Quartz, que precisa do `q.UseTimeZoneConverter()` opt-in com pacote externo.

### Deve adotar (do Quartz) — ainda não explícito nas specs

- **Padrão de consulta agenda∩calendário no cálculo do NextRun** (o `while(!IsTimeIncluded) proximo = agenda.Next(proximo)`). A spec 005 (scheduler) precisa embutir esse laço; documentar que o calendário é consultado pelo *scheduler*, não guardado no calendário.
- **`ICalendar`/`BaseCalendar` internos** com `IsTimeIncluded(DateTimeOffset)` como método primário — mesmo que a API pública seja só o `ICalendarBuilder`, internamente convém ter os tipos concretos (`HolidayCalendar`, `WeeklyCalendar`, `CronCalendar`) que o builder monta.
- **Encadeamento (base calendar)**: útil quando um calendário combina "feriados + fins de semana". Mesmo que a v1 não exponha isso no builder, deixe a estrutura interna suportar composição (ex.: um calendário como lista de regras de exclusão avaliadas em OR).
- **`TimeProvider` em tudo** — testes determinísticos (checklist + skill `dotnet-claude-kit:testing`).
- **Clonar/isolar** os objetos de calendário na entrada e saída do storage.
- **Guarda anti-loop** com um "ano de desistência" configurável.
- **Versionamento do payload** de calendário persistido (campo `schema`/`version` no JSON) para evolução futura.

### Pode melhorar (superar o Quartz)

- **Uma estrutura de regras única** em vez de 6 classes concretas: modelar o calendário como uma lista de `ExclusionRule` (`Date`, `DateRange`, `DaysOfWeek`, `Cron`) avaliadas em OR. Fica AOT-safe, serializa trivialmente em JSON e o `ICalendarBuilder` só acumula regras. Evita a fragilidade do `AnnualCalendar` (dependência de ordenação) e do `MonthlyCalendar` (dia 31).
- **Janela que cruza a meia-noite**: o `.EntreHorarios(22:00, 06:00)` do Guará (spec 038) é mais expressivo que o `DailyCalendar` do Quartz (que proíbe cruzar meia-noite). Mantenha isso — mas **fora** do sistema de calendários: janela do `ACada` é propriedade do recorrente, não um calendário.
- **`DateOnly`/`TimeOnly`** (tipos modernos) na API, em vez de `DateTime`/`DateTimeOffset` truncados como o Quartz faz — a spec 038 já usa `DateOnly`. Mais claro e sem ambiguidade de hora.
- **Fuso automático** (já decidido) — sem opt-in, sem pacote.
- **Mensagem de erro rica** ao remover calendário em uso: listar os recorrentes que o usam (spec 038 diz "erro listando os recorrentes"). O Quartz só diz "cannot be removed if referenced by a Trigger" — o Guará pode ser melhor.
- **Aviso observável** (log estruturado + dashboard) quando um calendário zera as ocorrências futuras — em vez de só retornar `MinValue` silenciosamente como o Quartz.

---

## Armadilhas e detalhes sutis a não perder na implementação

1. **O calendário não escalona — o scheduler escalona.** O calendário só responde `IsTimeIncluded`. O laço de "pular" vive no cálculo do NextRun (spec 005). Não coloque lógica de agenda dentro do calendário.
2. **Interseção agenda∩calendário:** use `IsTimeIncluded` + recomputar a agenda, **não** só `GetNextIncludedTimeUtc`. Avançar só pelo calendário pode cair num instante que a agenda não gera. O Quartz aprendeu isso — todos os triggers usam o laço `while(!cal.IsTimeIncluded(x)) x = GetFireTimeAfter(x)`.
3. **Fuso decide a data.** "Excluir 25/12" é ambíguo sem fuso: o instante UTC precisa ser convertido para o fuso do recorrente **antes** de extrair `.Date`/`.DayOfWeek`. Todos os calendários de dia inteiro do Quartz fazem `TimeZoneUtil.ConvertTime(x, TimeZone)` primeiro. No Guará o fuso vem do recorrente (`NoFusoHorario`).
4. **Precisão dia inteiro vs. instante.** `Holiday/Weekly/Monthly/Annual` operam em dia inteiro (zeram a hora); `Cron/Daily` operam no instante. Misturar sem cuidado gera bugs de fronteira (um disparo às 23:59 do dia excluído).
5. **Guarda anti-loop-infinito é obrigatória.** Um calendário que exclui tudo (ou quase) faz o laço nunca terminar. O Quartz corta em `YearToGiveUpSchedulingAt`. Sem essa guarda, o scheduler trava. No Guará: cortar e marcar o recorrente "sem próxima ocorrência" + aviso (já na spec 038).
6. **Recálculo só quando SUBSTITUI.** No `RAMJobStore`, o `updateTriggers` só roda `if (obj is not null && updateTriggers)` — ou seja, ao **substituir** um calendário existente, não ao criar. Ao criar, os recorrentes que forem associados depois já calcularão com o calendário certo. Faz sentido: não há o que recalcular num calendário recém-criado.
7. **Clonar na entrada e na saída.** Se você guardar/entregar a mesma instância, um `Excluir*` posterior mutaria o objeto no store sem passar pelo caminho de recálculo — inconsistência silenciosa.
8. **Isolamento sob lock.** O `RAMJobStore` faz tudo sob `SemaphoreSlim` (`lockObject`). O recálculo de todos os triggers acontece **dentro** do lock — no Guará, cuidado para não segurar lock caro durante um recálculo O(n·custo-cron); considere recalcular fora do lock e commitar sob transação (modelo "storage é a fila").
9. **`AnnualCalendar` frágil por ordenação** — não copie o `foreach` com early-return dependente de `SortedSet`. Use `HashSet<(int Month, int Day)>` e checagem direta.
10. **`MonthlyCalendar` e o dia 31** — excluir o dia 31 nunca casa em fevereiro/abril/etc.; documente ou valide.
11. **`DailyCalendar` não cruza meia-noite** — o Quartz lança se `start >= end`. A janela do Guará **pode** cruzar (é do `ACada`, não um calendário). Não confunda os dois mecanismos.
12. **Serialização versionada.** O Quartz lança em versões antigas de `HolidayCalendar` ("re-serialize com Quartz 2.x") — migração de esquema dói. No Guará (JSON + source gen), inclua um campo `version` no payload desde a v1 para não sofrer depois.
13. **`GetHashCode`/`Equals` corretos** importam se calendários entram em sets/dedupe; o Quartz os implementa em toda subclasse. Para JSON isso é menos crítico, mas dedupe de definição pode precisar.
14. **`ComputeFirstFireTimeUtc` respeita vigência.** Se `EndTime < agora`, retorna `null` (nunca dispara) — antes mesmo de consultar o calendário. No Guará, `TerminaEm` no passado ⇒ recorrente expirado (spec 038 AC-4). Ordem: vigência primeiro, calendário depois.
15. **`IniciaEm` no passado** ⇒ primeiro disparo é o próximo válido ≥ agora (o Quartz faz `if (nextFire < now) nextFire = GetFireTimeAfter(now)`). Espelhado na spec 038 Edge Cases.
16. **Misfire e calendário se combinam** no `UpdateWithNewCalendar`: ao pular datas para o futuro/passado, o Quartz ainda respeita o `misfireThreshold`. No Guará, a política de misfire (semantics.md: "roda UMA ocorrência de compensação") precisa conviver com o pular-por-calendário — o candidato compensatório também passa pelo calendário.
