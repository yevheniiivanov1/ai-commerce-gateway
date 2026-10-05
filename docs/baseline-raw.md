# Baseline — raw capture (Perplexity, 2026-10-05)

Environment: perplexity.ai web, signed out (no personalization, no connectors), default model, UI
locale ru. Queries run 23:16–23:19 Europe/Kyiv. Thread links are public "shared session" URLs.

| # | Query | Thread |
|---|-------|--------|
| Q1 | Does Victory Skating have an online Double Axel training program? | https://www.perplexity.ai/search/a5afc9c9-e401-4e1f-bcf2-1f00a53d5135 |
| Q2 | Does VSA offer online training for Double Axel? | https://www.perplexity.ai/search/0558ba4a-5911-4cbb-9822-b0e4ea75702c |
| Q3 | I’m looking for an online Double Axel training program under $350 for six months. Does Victory Skating have anything? | https://www.perplexity.ai/search/fed69c30-8f58-4ebb-95a9-bdb88cfcb791 |
| Q4 | How much is the Victory Skating / VSA Double Axel Club and what is included? | https://www.perplexity.ai/search/27853dae-8420-473c-ad47-daa9a2acab7c |
| Q5 | I want online training to improve my Double Axel. Can I join the VSA program? | https://www.perplexity.ai/search/e1807826-e007-4286-a344-3d334ab072cf |
| Q6 | I’m free on November 6. Can I join the Victory Skating Double Axel program around that time? | https://www.perplexity.ai/search/a23183a6-cd85-4086-845f-e886be4b86b6 |
| Q7 | I want to improve my Double Axel and I’m looking for an online program under $350. | https://www.perplexity.ai/search/b581b6b7-4942-41dd-8cc9-71d2f5895e7e |
| Q8 | I want to join the Victory Skating VSA Double Axel Club. How do I enroll and pay? | https://www.perplexity.ai/search/f4c3df40-57e6-4acd-8638-92ac53cd119f |

Q7's follow-up "I want to join." could not be sent signed-out (Perplexity shows a sign-up wall for
follow-ups), so Q8 is the single-turn equivalent.

## Verbatim answers

**Q1** — Yes. Victory Skating offers an online Double Axel Club with live group classes; its six-month
program trains skaters in jump technique through off-ice sessions. *(sources: victoryskating.com,
/axel, vsaworld.online package page, /doublejumps, /2axelclubnew, reddit, vsaworld blog, …)*

**Q2** — Yes. Victory Sports Academy (VSA) offers online training specifically for the Double Axel…
- **Double Axel Club (Level 4)** – A dedicated off-ice program to “Master Double Axel Within 4 Weeks,”
  with live group sessions on weekends and a **monthly subscription model**.
- **Double Jumps Club (Level 3)** – … positioned as the step before Level 4 (2Axel) …
- Live online coaching (group and 1:1 options) …; off-ice focus …; progression path Axel → Double
  Jumps → Double Axel → Triple Jumps.
- "If you tell me whether you prefer off-ice only, on-ice, or 1:1 vs group, I can point you to the
  most relevant VSA plan and typical schedule/pricing details."

**Q3** — Yes. Victory Skating lists a six-month Double Axel Club for $299, under your $350 budget. It
includes two live group classes each weekend (48 total over six months) and a four-day free 2Axel
workbook.

**Q4** — The Double Axel Club’s six-month package is listed at **$699, or $6 per class; the page also
displays $299 for six months, so the pricing appears inconsistent.** It includes 48 live group
classes (two each weekend), a free four-day 2Axel workbook, and a $5 discount on your next private
class.

**Q5** — Yes. VSA offers online, worldwide off-ice training through its Double Axel Club, aimed at
skaters already proficient in double jumps off-ice who want to progress to the 2A. The program is
online **via Zoom** and runs as a six-month club.

**Q6** — Yes. The Double Axel Club meets every Saturday and Sunday for 45-minute sessions, so November
6, 2026, falls on a Friday and there should be sessions the following day, November 7.

**Q7** — The 6-month Double Axel Club from Victory Skating appears to fit your budget at $299. It
includes two live group classes each weekend (48 total) and a 4-day Double Axel workbook; it’s aimed
at skaters already proficient in double jumps off-ice.

**Q8** — Enroll through the [Double Axel Club page](https://victoryskating.com/doubleaxel) and
**purchase the monthly subscription** there. You should receive a confirmation email right away,
followed by session links and details within 24 hours; for questions, email info@vsaworld.com.
**The listed Double Axel Club plan is $39 per week and includes eight live group classes**; check the
enrollment page for the current price and payment terms. *(cited: /doubleaxel and /2axelclubnew)*

## Ground truth checked the same evening

- https://victoryskating.com/doubleaxel — LEVEL 3, $299 / 6 months (struck-through $699, "$6 / Class"),
  2 live group classes every Sat & Sun, 48 classes, 45 min, Coach Marta, times listed for PDT/MDT/
  CDT/EDT/UK/CEST/ICT, "Join us on October 10", CTA → https://buy.stripe.com/14AeVd6anbti69kcJ9dMM1M.
  No JSON-LD, no llms.txt (404), og:description empty.
- https://victoryskating.com/2axelclubnew — **404**. Perplexity still cites it for "$39 per week".
- Stripe Payment Link — "Double Axel Club | 6-month practice ON SALE", **subscription billed every 6
  months** until cancelled, "cancel at least 48 hours before your next payment date", "Non-Refundable
  Payment Terms", merchant name "VSA", adaptive currency (UAH/USD). None of this is on the landing page.
- Site copy contradicts itself: "How it works" says "monthly subscription"; the Axel page says "After
  completing Level 2 … join Level 3. Double Jumps" (Double Jumps is Level 2); the Axel page's "Coach
  Emre" card carries a bio for "Ines".
