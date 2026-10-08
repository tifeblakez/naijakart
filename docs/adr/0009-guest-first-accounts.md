# ADR-0009: Guest-first accounts, phone claim, referrals

## Status
Accepted (2026-10-08)

## Context
The design canvas (screens 01.1, 21–25) has players race first and "save progress" after their
first race: phone number with an SMS or WhatsApp code, or Google/Apple sign-in, then a racer name,
look and home city, an account bonus, and a referral code that pays both sides. Ranked unlocks
after saving. PRD §67 leaves authentication to the backend.

## Decision
* Identity stays the `PlayerId` the client presents in `Hello`. A guest id is minted on the device;
  claiming attaches a provider (`phone` + E.164, or a social subject) to that same id, so coins,
  karts and rank carry over without migration.
* `Core/Accounts/AccountService` owns the rules (engine-agnostic, unit-tested): phone
  normalisation (Nigerian 0803… → +234803…), one-time codes with TTL, attempt limit and resend
  window, racer-name validation and uniqueness (case-insensitive, reserved words), home city from
  a configured list, referral codes (`TIFE-4K2` style) and the two rewards (account bonus on claim,
  referral bonus to both after the invited racer's first race). Rewards are ledger credits with
  idempotency keys.
* Delivery of codes is an interface (`IOtpSender`): the server ships a console sender for
  development; production binds an SMS/WhatsApp provider. Social tokens are verified by the
  backend before `ClaimWithProvider` reaches the game server.
* A phone or social subject that already owns a claimed account makes the request a sign-in: the
  server answers with `SignInPlayerId` and the client says `Hello` as that player. Guest progress
  is not merged automatically (the design's "Have an account? Sign in" path); a merge tool can
  come later.
* All numbers (code length, TTL, attempts, bonuses, name length, cities, the Ranked gate) live in
  `GameConfig.accounts`.

## Consequences
* "Later, keep racing" costs nothing: guests play Quick Race, Practice and private rooms; only
  Ranked and cross-device continuity need a claimed account.
* Phone numbers are personal data: the store keeps E.164 only for claimed accounts and pending
  claims; the wire never carries a full number back (masked).
