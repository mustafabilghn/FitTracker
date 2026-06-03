"""
FitTracker-AI — BLEU/ROUGE Evaluation Script
=============================================
Bu script FitBot'un ürettiği koçluk planlarını uzman referans planlarıyla
karşılaştırarak BLEU-4 ve ROUGE-L skorlarını hesaplar.

KURULUM (bir kez):
    pip install groq nltk rouge-score

ÇALIŞMA SIRASI:
    1. GROQ_API_KEY değişkenine kendi Groq API anahtarını yaz
    2. python evaluate_fitbot.py
    3. Sonuçları kopyalayıp buraya yapıştır

Groq API key almak için: https://console.groq.com/keys
"""

import os, json, time, sys
from reference_plans import REFERENCE_PLANS

# ──────────────────────────────────────────────────────────────────
# AYARLAR — sadece API key gir
# ──────────────────────────────────────────────────────────────────
GROQ_API_KEY = ""   # ← sadece bunu değiştir
MODEL         = "llama-3.3-70b-versatile"
TEMPERATURE   = 0.3
MAX_TOKENS    = 800
# ──────────────────────────────────────────────────────────────────

try:
    from groq import Groq
except ImportError:
    print("HATA: groq kütüphanesi bulunamadı.")
    print("Çalıştır: pip install groq")
    sys.exit(1)

try:
    import nltk
    from nltk.translate.bleu_score import corpus_bleu, SmoothingFunction
    nltk.download('punkt', quiet=True)
    nltk.download('punkt_tab', quiet=True)
except ImportError:
    print("HATA: nltk kütüphanesi bulunamadı.")
    print("Çalıştır: pip install nltk")
    sys.exit(1)

try:
    from rouge_score import rouge_scorer
except ImportError:
    print("HATA: rouge-score kütüphanesi bulunamadı.")
    print("Çalıştır: pip install rouge-score")
    sys.exit(1)


# ──────────────────────────────────────────────────────────────────
# PROMPT ŞABLONU  (AiWorkoutCoachService.cs ile aynı yapı)
# ──────────────────────────────────────────────────────────────────
def build_prompt(plan: dict) -> str:
    goal_map = {
        "strength":    "güç geliştirme",
        "hypertrophy": "kas kütlesi artırma",
        "weight_loss": "kilo verme",
        "endurance":   "dayanıklılık geliştirme",
    }
    goal_tr = goal_map.get(plan["goal"], plan["goal"])

    # Plan ID'den gerçekçi mock antrenman verisi üret
    pid = plan["id"]
    seed = sum(ord(c) for c in pid)

    workouts_desc = _mock_workouts(plan["goal"], seed)

    system_msg = (
        "Sen deneyimli bir kişisel fitness koçusun. "
        "Görevin kullanıcının antrenman geçmişini analiz edip Türkçe koçluk raporu üretmek. "
        "YANIT MUTLAKA VE SADECE TÜRKÇE OLMALI. "
        "İngilizce, Almanca veya başka dil KESINLIKLE YASAK. "
        "Yanıtını aşağıdaki JSON formatında ver:\n"
        "{\n"
        '  "ozet": "kısa özet (2-3 cümle)",\n'
        '  "guclu_yonler": ["madde1", "madde2", "madde3"],\n'
        '  "gelisim_alanlari": ["madde1", "madde2", "madde3"],\n'
        '  "sonraki_antrenman": "bugün veya yarın için öneri"\n'
        "}"
    )

    user_msg = (
        f"Adım 1 — Bağlam: Kullanıcının hedefi {goal_tr}.\n\n"
        f"Adım 2 — Antrenman geçmişi:\n{workouts_desc}\n\n"
        f"Adım 3 — Güvenlik: ACSM kılavuzlarına uygun, güvenli öneriler yap.\n\n"
        f"Adım 4 — Çıktı: Yukarıdaki JSON formatında Türkçe koçluk raporu üret."
    )
    return system_msg, user_msg


def _mock_workouts(goal: str, seed: int) -> str:
    """Hedef tipine göre gerçekçi antrenman özeti üret."""
    templates = {
        "strength": [
            "Son 4 hafta: Squat 3x5 @{w1}kg, Bench Press 3x5 @{w2}kg, Deadlift 1x5 @{w3}kg. "
            "Haftalık 3 seans. Son 2 haftada Bench Press'te plato var.",
            "Haftada 3 gün güç antrenmanı. Squat @{w1}kg, Bench @{w2}kg, Deadlift @{w3}kg. "
            "12 seans, düzenli progresyon.",
        ],
        "hypertrophy": [
            "PPL programı, haftada 6 gün. Rep aralığı 8-12. "
            "Bench Press @{w2}kg x10, Squat @{w1}kg x10, Barbell Row @{w3}kg x10. Hacim odaklı.",
            "Bölünmüş program: göğüs/kol, sırt/omuz, bacak. 4x10-12 rep. "
            "Bench @{w2}kg, Squat @{w1}kg, Deadlift @{w3}kg.",
        ],
        "weight_loss": [
            "Haftalık 4-5 seans. Ağırlık antrenmanı + 30dk kardiyovasküler. "
            "Son 1 ayda 2 kg kayıp. Vücut ağırlığı {w4}kg.",
            "Full body devreler + HIIT. Haftada 4 gün aktif. "
            "Kalori açığı programı, Squat @{w1}kg x15, Dumbbell Row @{w2}kg x15.",
        ],
        "endurance": [
            "Haftada 3-4 kez koşu. Haftalık mesafe {w4}km. "
            "Uzun koşu 45-60dk, interval 2x/hafta.",
            "Bisiklet + koşu çapraz antrenmanı. Kardiyovasküler kapasite artıyor. "
            "Son ayda {w4}km toplam koşu mesafesi.",
        ],
    }
    import random
    random.seed(seed)
    w1 = random.choice([60, 70, 80, 90, 100, 110, 120])
    w2 = random.choice([50, 60, 70, 80, 90, 100])
    w3 = random.choice([80, 90, 100, 110, 120, 130, 140])
    w4 = random.choice([20, 30, 40, 50, 68, 72, 75, 80])

    t = random.choice(templates.get(goal, templates["strength"]))
    return t.format(w1=w1, w2=w2, w3=w3, w4=w4)


# ──────────────────────────────────────────────────────────────────
# GROQ API — FitBot çıktısı üret
# ──────────────────────────────────────────────────────────────────
def call_groq(client, system_msg: str, user_msg: str) -> dict:
    """Groq API çağrısı yap, JSON parse et."""
    resp = client.chat.completions.create(
        model=MODEL,
        temperature=TEMPERATURE,
        max_tokens=MAX_TOKENS,
        messages=[
            {"role": "system", "content": system_msg},
            {"role": "user",   "content": user_msg},
        ],
    )
    raw = resp.choices[0].message.content.strip()

    # JSON çıkar
    if "```" in raw:
        raw = raw.split("```")[1]
        if raw.startswith("json"):
            raw = raw[4:]
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        # Manuel parse fallback
        return {"ozet": raw, "guclu_yonler": [], "gelisim_alanlari": [], "sonraki_antrenman": ""}


# ──────────────────────────────────────────────────────────────────
# METİN BİRLEŞTİR (karşılaştırma için düz metin)
# ──────────────────────────────────────────────────────────────────
def plan_to_text(plan_dict: dict) -> str:
    """Plan dict'ini düz Türkçe metne çevir."""
    parts = []

    ozet = plan_dict.get("ozet", "")
    if ozet:
        parts.append(ozet)

    for key in ("guclu_yonler", "gelisim_alanlari"):
        items = plan_dict.get(key, [])
        if isinstance(items, list):
            parts.extend(items)
        elif isinstance(items, str):
            parts.append(items)

    sonraki = plan_dict.get("sonraki_antrenman", "")
    if sonraki:
        parts.append(sonraki)

    return " ".join(parts)


def tokenize(text: str):
    """Basit Türkçe tokenizer (whitespace)."""
    import re
    text = text.lower()
    text = re.sub(r'[^\w\s]', ' ', text, flags=re.UNICODE)
    return text.split()


# ──────────────────────────────────────────────────────────────────
# ANA DEĞERLENDİRME
# ──────────────────────────────────────────────────────────────────
def main():
    if GROQ_API_KEY == "BURAYA_GROQ_API_KEY_YAZ":
        print("=" * 60)
        print("HATA: Groq API key girilmemiş!")
        print("evaluate_fitbot.py dosyasını aç,")
        print("GROQ_API_KEY değişkenine anahtarını yaz.")
        print("=" * 60)
        sys.exit(1)

    print("=" * 60)
    print("FitTracker-AI — BLEU/ROUGE Evaluation")
    print(f"Model: {MODEL}  |  Plans: {len(REFERENCE_PLANS)}")
    print("=" * 60)

    client   = Groq(api_key=GROQ_API_KEY)
    scorer   = rouge_scorer.RougeScorer(["rougeL"], use_stemmer=False)
    smoother = SmoothingFunction().method1

    references_corpus  = []   # BLEU için
    hypotheses_corpus  = []
    rouge_scores       = []
    results            = []

    for i, plan in enumerate(REFERENCE_PLANS):
        print(f"\n[{i+1:02d}/{len(REFERENCE_PLANS)}] {plan['id']}...")

        # Referans metin
        ref_text  = plan_to_text(plan)
        ref_tokens = tokenize(ref_text)

        # FitBot'tan çıktı al
        system_msg, user_msg = build_prompt(plan)
        try:
            generated = call_groq(client, system_msg, user_msg)
        except Exception as e:
            print(f"  !! API hatası: {e}")
            generated = {"ozet": "", "guclu_yonler": [], "gelisim_alanlari": [], "sonraki_antrenman": ""}

        hyp_text   = plan_to_text(generated)
        hyp_tokens = tokenize(hyp_text)

        # BLEU için birikt
        references_corpus.append([ref_tokens])
        hypotheses_corpus.append(hyp_tokens)

        # ROUGE-L
        rs = scorer.score(ref_text, hyp_text)
        rouge_scores.append(rs["rougeL"].fmeasure)

        # Bireysel sonuç
        individual_bleu = corpus_bleu([[ref_tokens]], [hyp_tokens],
                                      smoothing_function=smoother)
        results.append({
            "id":       plan["id"],
            "goal":     plan["goal"],
            "bleu4":    round(individual_bleu, 4),
            "rougeL":   round(rs["rougeL"].fmeasure, 4),
            "hyp_preview": hyp_text[:120] + "...",
        })

        print(f"  BLEU-4: {individual_bleu:.3f}  |  ROUGE-L: {rs['rougeL'].fmeasure:.3f}")
        time.sleep(0.3)  # rate limit

    # ── Corpus BLEU (tüm 20 plan üzerinden)
    corpus_bleu4 = corpus_bleu(references_corpus, hypotheses_corpus,
                               weights=(0.25, 0.25, 0.25, 0.25),
                               smoothing_function=smoother)
    avg_rouge = sum(rouge_scores) / len(rouge_scores)

    # ── Hedef tipine göre ortalamalar
    goals = ["strength", "hypertrophy", "weight_loss", "endurance"]
    goal_bleu  = {}
    goal_rouge = {}
    for g in goals:
        g_results = [r for r in results if r["goal"] == g]
        if g_results:
            goal_bleu[g]  = sum(r["bleu4"]  for r in g_results) / len(g_results)
            goal_rouge[g] = sum(r["rougeL"] for r in g_results) / len(g_results)

    # ── Sonuç raporu
    print("\n" + "=" * 60)
    print("SONUÇLAR")
    print("=" * 60)
    print(f"Corpus BLEU-4 : {corpus_bleu4:.4f}  (hedef ≥ 0.35)")
    print(f"Avg ROUGE-L   : {avg_rouge:.4f}  (hedef ≥ 0.45)")
    print()
    print("Hedef tipine göre:")
    for g in goals:
        print(f"  {g:15s}  BLEU: {goal_bleu.get(g,0):.3f}  ROUGE-L: {goal_rouge.get(g,0):.3f}")
    print()
    print("Hedefler:")
    b_ok = "✓ BAŞARILI" if corpus_bleu4 >= 0.35 else "✗ BAŞARISIZ (hedef: 0.35)"
    r_ok = "✓ BAŞARILI" if avg_rouge   >= 0.45 else "✗ BAŞARISIZ (hedef: 0.45)"
    print(f"  BLEU-4  ≥ 0.35 : {b_ok}")
    print(f"  ROUGE-L ≥ 0.45 : {r_ok}")
    print("=" * 60)

    # ── JSON kaydet
    output = {
        "corpus_bleu4": round(corpus_bleu4, 4),
        "avg_rougeL":   round(avg_rouge, 4),
        "by_goal": {
            g: {"bleu4": round(goal_bleu.get(g,0), 4),
                "rougeL": round(goal_rouge.get(g,0), 4)}
            for g in goals
        },
        "per_plan": results,
    }
    with open("evaluation_results.json", "w", encoding="utf-8") as f:
        json.dump(output, f, ensure_ascii=False, indent=2)

    print(f"\nDetaylı sonuçlar: evaluation_results.json")
    print("\n--- TEZE YAZILACAK DEĞERLER ---")
    print(f"BLEU-4  = {corpus_bleu4:.2f}")
    print(f"ROUGE-L = {avg_rouge:.2f}")
    print("Bu değerleri kopyalayıp Claude'a yapıştır.")


if __name__ == "__main__":
    main()
