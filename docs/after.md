# After: Perplexity with the gateway connected

**Read this first.** Asking ordinary Perplexity the baseline questions again will give the same
answers as before, and that is expected. Perplexity's own index changes only when the merchant
serves the new data from `victoryskating.com` (the JSON-LD snippet from `/programs/{slug}/jsonld`
pasted into each Tilda page, and `llms.txt` on the domain) and Perplexity recrawls it. This
prototype runs on a third-party host and is deliberately `noindex`, so it can't change organic
answers and shouldn't try to.

The "after" therefore shows the two mechanisms the gateway adds, each in Perplexity where possible:

1. **Perplexity answering from the gateway's facts.** The brief's sales dialogue, with the
   generated fact sheet in context: what Perplexity's retrieval will hand its model once the merchant
   publishes the layer and it is indexed. No tools are involved; the answer still ends in the
   checkout link from the fact sheet.
2. **An assistant acting through the MCP tools.** Search → availability → `start_enrollment` →
   tracked redirect to Stripe → funnel. In Perplexity this needs a paid plan (custom connectors), so
   it is shown with an MCP client against the live server, plus the end-to-end test that runs it on
   every CI build.

## 1. Perplexity answering from the gateway's facts

**First attempt: point Perplexity at the live layer.** It doesn't work on the free plan, and the
reason matters. Signed out, Perplexity refuses to open a URL from the prompt ("Sign up and repeat
your request"). Signed in, it replies "The exact URL could not be retrieved directly" and answers from
its index instead, which brings back the stale "$39 per week" offer. The gateway's read log
(`/api/readers`) shows that no request from Perplexity ever arrived. A copy of the same file on GitHub
fails the same way, while long-indexed pages such as Stripe's docs "work". The free plan answers
from its index; it doesn't fetch new pages. That is also why organic answers change only after the
merchant serves the layer from their own domain and Perplexity recrawls it.

**So the indexed state is simulated.** The first message carries the generated fact sheet
([`/programs/double-axel-club.md`](https://vsa-ai-gateway.onrender.com/programs/double-axel-club.md),
verbatim apart from the time-zone table), which is what retrieval would hand the model. The brief's
dialogue then runs in the same thread (perplexity.ai, free plan, signed in, 2026-10-06):

| Turn | Perplexity today ([baseline](baseline.md)) | Perplexity with the gateway's facts |
|---|---|---|
| "I want to improve my Double Axel and I'm looking for an online program under $350." | $299 and nothing more; other threads say "$699 … pricing appears inconsistent" | the 6-Month Double Axel Club, "$299 for the six-month term — within your $350 limit"; a table with "$299 every 6 months, auto-renewing unless cancelled", Saturdays and Sundays 07:00–07:45 Pacific, Coach Marta, Level 3 entry requirement, about $6.23 per class; a fit check sending skaters without consistent doubles to the Double Jumps Club; the free 4-day plan |
| "I'm free on November 6. Can I join the program around that time?" | "there should be sessions the following day", no times | "Yes … enrollment is rolling … there is no fixed cohort start or enrollment deadline"; November 6 is a Friday, so Saturday November 7 and Sunday November 8, 07:00–07:45 Pacific; "The 'Join us on October 10' wording is only a marketing label … not a cutoff date"; renewal and refund terms before enrolling |
| "I want to join." | "purchase the monthly subscription", "$39 per week", a link to the landing page | "You can enroll directly through VSA's secure checkout … **start enrollment**", linking `/checkout/vsa-double-axel-club-6m` (→ the Stripe page above), with $299, renewal every six months, the 48-hour cancellation window and non-refundable payments |

![Perplexity: program under $350](screenshots/after/perplexity-1-program-under-350.jpg)

![Perplexity: November 6](screenshots/after/perplexity-2-november-6.jpg)

![Perplexity: I want to join](screenshots/after/perplexity-3-i-want-to-join.jpg)

The "start enrollment" link goes through the gateway's `/checkout` redirect. A click that arrives
from perplexity.ai is attributed to channel `perplexity` by its Referer (covered by
`A_plain_checkout_link_is_attributed_by_its_referrer`).

## 2. The tool flow, against the live server

Run on 2026-10-06 against https://vsa-ai-gateway.onrender.com/mcp, with the official C# MCP SDK as
the client ([`tools/mcp-demo.cs`](../tools/mcp-demo.cs)). It sends the same requests and gets the same
JSON a Perplexity custom connector would. Full transcript: [after/mcp-transcript.md](after/mcp-transcript.md).

| The brief's step | Tool call | What came back | Before (Perplexity today) |
|---|---|---|---|
| "I want to improve my Double Axel … under $350." | `search_programs(query, maxPrice: 350)` | the Double Axel Club only; **$299 billed every 6 months until cancelled (auto-renews)**; every weekend 07:00–07:45 Pacific; Coach Marta; within budget | $299, but no coach, times or terms |
| "How much is it and what is included?" | `get_program_details` | $299; **"$699 is the struck-through former price … not what the buyer pays"**; Level 3 and who it's for; all 7 inclusions; cancel ≥48 h before the next payment; non-refundable | "$699 … pricing appears inconsistent" |
| "I'm free on November 6. Can I join around then?" | `check_availability(date: 2026-11-06)` | Nov 6 is a Friday; nearest classes **Sat Nov 7 and Sun Nov 8, 10:00–10:45 New York time**; enroll by Fri Nov 6 10:00; "Join us on October 10" is not a deadline | Sat/Sun, but no times and no way to enroll |
| "I want to join." | `start_enrollment` | **checkoutUrl**, eligibility (Level 3; else Double Jumps Club; free 4-day plan), four disclosures, first class Sat Nov 7 | "$39 per week", "monthly subscription", a link to the landing page |
| the user opens the link | `GET /checkout/vsa-double-axel-club-6m?ref=enr_…` | **302** → `buy.stripe.com/…?client_reference_id=enr_…&utm_source=…&utm_medium=ai-assistant` | — |
| — | `GET /api/funnel` | `EnrollmentStarted` → `CheckoutOpened` for that reference | — |

The checkout the link lands on is the merchant's real Stripe page, with our reference in the URL
(shown in USD; the page localises the language):

![Stripe checkout reached through the gateway's link](screenshots/after/stripe-checkout-after-redirect.jpg)

The fact sheet the assistant and crawlers read
([live](https://vsa-ai-gateway.onrender.com/programs/double-axel-club)):

![Live fact sheet](screenshots/after/fact-sheet.jpg)

## Reproduce it

You need a Perplexity plan that includes custom connectors (Pro, Max or Enterprise, as of 2026).

1. **Add the connector.** perplexity.ai → Settings → **Connectors** → **+ Custom connector** →
   **Remote**.
   - Name: `Victory Skating (VSA)`
   - MCP server URL: `https://vsa-ai-gateway.onrender.com/mcp`
   - Authentication: **None**
   - Transport: **Streamable HTTP**
   - Tick the risk acknowledgement → **Add**, then enable the connector.
2. **Start a new thread** and make sure the connector is switched on for it (sources/tools menu
   under the input box).
3. **Ask the brief's questions**, one thread each:
   - Does Victory Skating have an online Double Axel training program?
   - Does VSA offer online training for Double Axel?
   - I'm looking for an online Double Axel training program under $350 for six months. Does Victory Skating have anything?
   - How much is the Victory Skating / VSA Double Axel Club and what is included?
   - I want online training to improve my Double Axel. Can I join the VSA program?
   - I'm free on November 6. Can I join the Double Axel program around that time?
4. **Run the sales flow** in one thread:
   1. I want to improve my Double Axel and I'm looking for an online program under $350.
   2. I want to join.

   Expected: the second answer contains a link `https://vsa-ai-gateway.onrender.com/checkout/vsa-double-axel-club-6m?ref=enr_…&channel=perplexity`
   plus the renewal, cancellation and refund terms. Opening it lands on the merchant's Stripe page
   ("Double Axel Club | 6-month practice ON SALE"). **Don't pay**: it is the live checkout.
5. **See the trace.** `https://vsa-ai-gateway.onrender.com/api/funnel` shows `EnrollmentStarted` and `CheckoutOpened` for
   that reference with channel `perplexity`. Perplexity's answer also lists the tool calls it made.

Without a Perplexity plan, the same server works in any MCP client, for example the
[MCP Inspector](https://github.com/modelcontextprotocol/inspector):

```bash
npx @modelcontextprotocol/inspector
```

Choose transport "Streamable HTTP" and URL `https://vsa-ai-gateway.onrender.com/mcp`.
