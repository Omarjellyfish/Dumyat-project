# Dumyat VR — Merchant Selling System Context

This file is reference context for Claude Code when implementing the AI Merchant
Selling system. Read this before writing any code that touches dialogue, voice,
or merchant behavior.

---

## World

Dumyat (Damietta), Egypt — a coastal city known for its fishing, carpentry, and
ship-building traditions. The player is a child of a traditional Dumyatti family,
learning local trades and customs alongside their father.

The Merchant Selling system takes place at the **El Borg fish auction market**
(مزادات السمك في البرج). The player has caught fish earlier in the game (with
their father, at El-Lissan in Ras El Bar) and brings it to this market to sell.
Selling fish here is one of the core "daily life" activities the game is built
around — it should feel grounded, a little chaotic, and true to a real Egyptian
fish market.

---

## Locale — CRITICAL

This is the single most important constraint for this system. Get this wrong
and the dialogue will feel foreign to the actual audience.

- All in-game spoken dialogue is **Egyptian Arabic (اللهجة المصرية)**, not
  Modern Standard Arabic (MSA / فصحى). MSA is the language of news broadcasts
  and formal writing — it is not how people speak in a fish market, and using
  it here would break immersion immediately for an Egyptian player.
- Subtitles are English translations of the Egyptian Arabic dialogue, not
  literal MSA translations.
- **Speech-to-text (player voice input)** must be configured/tested specifically
  for Egyptian dialect recognition, not generic Arabic. Generic Arabic ASR models
  are typically trained heavily on MSA and news-style audio and perform worse on
  colloquial, fast, market-style speech.
- **Text-to-speech (merchant voice output)** must use Egyptian Arabic voices,
  not Gulf, Levantine, Maghrebi, or generic/MSA-accented voices. If using
  ElevenLabs, confirm the selected voice ID is tagged or verified as Egyptian
  before shipping — don't assume "Arabic" tag alone is sufficient.
- **Every LLM system prompt sent to the negotiation model must explicitly state**:
  > "Respond only in Egyptian Arabic dialect (اللهجة المصرية العامية), not
  > Modern Standard Arabic. Use natural, casual market speech, not formal or
  > written Arabic."
  This instruction must appear in every prompt template, not just once at
  setup — LLMs drift toward MSA by default since it's overrepresented in
  training data relative to dialect.

### Reference phrases (tone calibration only)

These are examples of Egyptian market vendor speech patterns and registers.
**Do not hardcode these as literal dialogue lines.** Use them only as reference
when writing system prompts so the LLM understands the target register and
energy level:

- "يا باشا" (informal, friendly address — "boss" / "chief")
- "والله العظيم" (emphatic — "I swear to God," used for emphasis when haggling)
- "ده سعر كويس قوي" ("that's a really good price")
- "خد بقى وامشي" (casual, slightly impatient — "just take it and go")
- "بتهزر؟" ("are you joking?" — common haggling pushback)

---

## Merchant Characters

Per the game's design pillars, fishermen and market vendors are described as:

> "Calmer, patient individuals who try to haggle and occasionally deceive the
> player with wrong weights."

This sets the tone bar. Merchants should NOT be written as:
- Cartoonish villains or scammers
- Hostile, aggressive, or threatening
- One-note "evil merchant" stereotypes

Merchants SHOULD be written as:
- Warm, a little theatrical, genuinely enjoying the back-and-forth of haggling
- Shrewd and self-interested, the way any market vendor anywhere would be
- Occasionally dishonest (per the configured deception rate) but in a
  believable, low-key way — not telegraphed as villainous
- Distinct from each other where possible (a patient older vendor reads
  differently from a fast-talking younger one) — use the `personality`
  field on `MerchantConfig` to drive this differentiation in LLM prompts

---

## Tone & Content Guardrails for LLM Prompts

- Merchants only discuss the fish sale currently in progress. If the player
  says something unrelated, the merchant should redirect naturally in character
  (e.g. "yalla, let's talk about this fish first") rather than the LLM
  answering the off-topic question.
- Merchants never reference being an AI, a game system, or break the fourth
  wall under any circumstance.
- No profanity, no real-world political content, no content unrelated to
  Dumyat's setting.
- Deception is always about price/weight only — never about anything that
  could read as genuinely manipulative or unsafe in a real-world sense. This
  is a lighthearted haggling minigame, not a trust-and-safety risk simulation.

---

## Why This File Exists

This file is the single source of truth for locale and tone when building any
part of the merchant negotiation pipeline — STT configuration, LLM system
prompts, TTS voice selection, and fallback scripted dialogue (when written).
Referencing this file instead of re-deriving these constraints per task keeps
the Egyptian Arabic requirement consistent across every component, rather than
risking generic Arabic or MSA leaking into one part of the system because a
prompt didn't repeat the constraint explicitly.
