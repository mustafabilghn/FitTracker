# FitTracker.Benchmark — FitBot Chat Latency Benchmark

Ölçüm-amaçlı, bağımsız bir konsol aracı. `POST /api/workout/fitbot/chat` uç noktasına gerçek
HTTP istekleri gönderir ve gecikmeyi üç bileşene ayırarak ölçer. Bu araç yalnızca **ölçüm**
yapar; hiçbir üretim davranışını değiştirmez.

## Ölçülen metrikler

| Metrik | Nasıl ölçülür | Her ortamda mevcut mu? |
|---|---|---|
| End-to-end API response time | İstemci tarafında (bu araçta) `Stopwatch`; tüm HTTP isteği/yanıtını sarar | Evet — her zaman ölçülebilir |
| Context retrieval / construction | Sunucu tarafında `AiWorkoutCoachService.ChatAsync` içinde `Stopwatch`; `X-FitBot-Context-Ms` header'ı ile taşınır | Yalnızca bu enstrümantasyonu içeren bir API build'i çalışıyorsa |
| Groq API inference | Aynı şekilde sunucu tarafında ölçülür, `X-FitBot-Groq-Ms` header'ı ile taşınır | Yalnızca bu enstrümantasyonu içeren bir API build'i çalışıyorsa |
| Context cache hit/miss | `X-FitBot-Context-Cache: hit\|miss` header'ından okunur | Aynı koşul |

Header'lar mevcut değilse (örneğin bu değişikliği içermeyen daha eski bir deploy'a karşı
çalıştırıyorsanız), araç o alt metriği **uydurmaz** — JSON/Markdown raporunda açık bir
"ölçülemedi" notu bırakır ve nedenini yazar.

## Üretim koduna eklenen enstrümantasyon (yalnızca ölçüm)

`FitTracker.API/Services/AiWorkoutCoachService.cs` içinde `ChatAsync` metoduna:
- context çözümleme (cache lookup + gerekirse DB sorgusu) ve Groq HTTP çağrısı etrafına
  `Stopwatch` eklendi,
- bu süreler + cache hit/miss durumu, yanıt gövdesi **değiştirilmeden**, üç yeni response
  header'ı olarak (`X-FitBot-Context-Ms`, `X-FitBot-Context-Cache`, `X-FitBot-Groq-Ms`) ve tek
  satırlık bir `ILogger` bilgi log'u olarak yayınlandı.

Reply içeriği, guardrail mantığı, cache TTL/anahtarları, hata davranışı (429 mesajı, diğer
durumlarda exception) ve JSON response şeması **aynen korunmuştur**. `Program.cs`'e yalnızca
`builder.Services.AddHttpContextAccessor();` satırı eklendi (header'ları yazabilmek için).

## Ön koşullar

1. FitTracker API çalışıyor olmalı (dev: `dotnet run --project FitTracker.API`, veya production URL).
2. API tarafında `Groq:ApiKey` yapılandırılmış olmalı — bu araç Groq'u doğrudan çağırmaz,
   API üzerinden dolaylı olarak tetikler.
3. Bir JWT'ye ihtiyacınız var. Üç seçenek:
   - `--token <jwt>`: hazır bir token kullan (**production için önerilen yol** — yeni sahte
     kullanıcı oluşturmaz).
   - `--email <e> --password <p>`: var olan bir hesapla `api/auth/login` üzerinden giriş yap.
   - Hiçbiri verilmezse: araç `api/auth/register` ile rastgele bir throwaway benchmark
     kullanıcısı oluşturur (`fitbot-bench-<guid>@bench.fittracker.invalid`). Bu, API'nin
     gerçek kullanıcı veritabanına yazar — dev ortamında sorun değildir, production'da dikkatli
     kullanın.

## Kullanım

```bash
dotnet run --project FitTracker.Benchmark -- --base-url https://localhost:7100 --requests 20 --warmup 2 --insecure
```

Yaygın seçenekler:

```
--base-url <url>                  API base URL (varsayılan: https://localhost:7100)
--requests <n>                    Toplanacak BAŞARILI ölçüm sayısı (varsayılan: 20) — raporlanan istatistikler bunlara dayanır
--warmup <n>                      Ölçümden önce atılan, rapora ASLA dahil edilmeyen warm-up isteği sayısı (varsayılan: 0)
--max-attempts <n>                Ölçüm aşamasında toplam deneme üst sınırı (varsayılan: requests × 3)
--message <text>                  Sabit FitBot mesajı (karşılaştırılabilirlik için sabit tutulur)
--action-type <type>               free|analyze|today|program|motivation (varsayılan: free)
--token <jwt>                     Hazır JWT kullan
--email <e> --password <p>        Var olan hesapla login ol
--fresh-user-every-request        Her istek için yeni kullanıcı → context cache her seferinde MISS
--interval-ms <ms>                İstekler arası bekleme (varsayılan: 250)
--insecure                        TLS sertifika doğrulamasını atla (yerel https dev sertifikası için)
--out-dir <path>                  Rapor klasörü (varsayılan: results)
```

## Warm-up ve "en az 20 başarılı ölçüm" garantisi

`--requests` bir deneme sayısı değil, **toplanacak başarılı ölçüm hedefidir**. Araç, hedefe
ulaşana kadar (veya `--max-attempts` güvenlik sınırına çarpana kadar) istek atmaya devam eder;
başarısız denemeler ayrıca sayılır ve error rate hesabına girer, ama hedef sayıya dahil edilmez.

`--warmup <n>` ile belirtilen istekler, ölçüm döngüsünden ÖNCE ve TAMAMEN AYRI çalışır:
sonuçları konsola loglanır ama `samples` listesine hiç eklenmez, dolayısıyla JSON/Markdown
raporundaki hiçbir istatistiğe (mean/median/min/max/p95, cache hit/miss, error rate) katkıda
bulunmaz. Bu, ilk isteklerdeki soğuk-başlangıç gürültüsünü (ilk DB sorgusu, JIT ısınması, TLS
handshake) ölçümden dışlamak için kullanılır.

Örnek: en az 20 temiz ölçüm için 2 warm-up + 20 ölçüm hedefiyle çalıştırma:

```bash
dotnet run --project FitTracker.Benchmark -- --token <JWT> --warmup 2 --requests 20
```

Konsol çıktısında warm-up istekleri `[warmup n/N]` öneki ile, ölçüm istekleri `[deneme n, başarılı m/M]`
formatıyla ayrı ayrı görünür; rapor dosyalarının başlığında da warm-up sayısı ve kaçının
başarısız olduğu ayrıca belirtilir.

## Cache hit/miss ayrımı nasıl elde edilir

Context cache TTL'i 1 dakikadır (`AiWorkoutCoachService.ContextCacheDuration`). Varsayılan
çalıştırmada (tek paylaşılan kullanıcı):
- 1. istek genelde **miss** olur (context henüz cache'lenmemiş),
- sonraki istekler, toplam süre 1 dakikayı aşmadığı sürece genelde **hit** olur.

Sadece **miss** davranışını izole ölçmek isterseniz `--fresh-user-every-request` ile çalıştırın
— her istek yeni bir kullanıcı için cache'i garantili olarak boşaltır. Araç hiçbir durumda
hit/miss oranını zorlamaz veya varsaymaz; her örnek gerçek `X-FitBot-Context-Cache` header
değerine göre sınıflandırılır. Bir bucket'ta örneklem sayısı azsa (n<5) rapor bunu açıkça not
düşer.

## Çıktılar

Her çalıştırma `--out-dir` altına zaman damgalı iki dosya yazar:
- `fitbot-latency-<timestamp>.json` — ham örnekler + tüm özet istatistikler
- `fitbot-latency-<timestamp>.md` — okunabilir Markdown tablo raporu

Her iki dosya da: genel istatistikler (mean/median/min/max/p95), cache hit/miss kırılımı,
başarısız istek sayısı + hata oranı, ve "ölçülemedi" notlarını içerir.

İstatistik yöntemi: p95/medyan, artan sıralı örneklerde nearest-rank yöntemiyle hesaplanır
(`ceil(p * n)`. indeks).
