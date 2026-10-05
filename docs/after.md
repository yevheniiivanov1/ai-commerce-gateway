# After: Perplexity with the gateway connected

**Read this first.** Asking ordinary Perplexity the baseline questions again will give the same
answers as before, and that is expected. Perplexity's own index changes only when the merchant
serves the new data from `victoryskating.com` (the JSON-LD snippet from `/programs/{slug}/jsonld`
pasted into each Tilda page, and `llms.txt` on the domain) and Perplexity recrawls it. This
prototype runs on a third-party host and is deliberately `noindex`, so it can't change organic
answers and shouldn't try to.

The "after" therefore shows the two mechanisms the gateway adds, each in Perplexity where possible:

1. **Perplexity reading the AI-ready layer.** The same questions, with the gateway's
   `llms-full.txt` in context. This is what Perplexity will see once the merchant publishes the layer
   on their domain. No tools are involved; the answer can still offer the checkout link from the
   fact sheet.
2. **An assistant acting through the MCP tools.** Search → availability → `start_enrollment` →
   tracked redirect to Stripe → funnel. In Perplexity this needs a paid plan (custom connectors), so
   it is shown with an MCP client against the live server, plus the end-to-end test that runs it on
   every CI build.

<!-- AFTER_RESULTS -->

## Reproduce it

You need a Perplexity plan that includes custom connectors (Pro, Max or Enterprise, as of 2026).

1. **Add the connector.** perplexity.ai → Settings → **Connectors** → **+ Custom connector** →
   **Remote**.
   - Name: `Victory Skating (VSA)`
   - MCP server URL: `<PUBLIC_URL>/mcp`
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

   Expected: the second answer contains a link `<PUBLIC_URL>/checkout/vsa-double-axel-club-6m?ref=enr_…&channel=perplexity`
   plus the renewal, cancellation and refund terms. Opening it lands on the merchant's Stripe page
   ("Double Axel Club | 6-month practice ON SALE"). **Don't pay**: it is the live checkout.
5. **See the trace.** `<PUBLIC_URL>/api/funnel` shows `EnrollmentStarted` and `CheckoutOpened` for
   that reference with channel `perplexity`. Perplexity's answer also lists the tool calls it made.

Without a Perplexity plan, the same server works in any MCP client, for example the
[MCP Inspector](https://github.com/modelcontextprotocol/inspector):

```bash
npx @modelcontextprotocol/inspector
```

Choose transport "Streamable HTTP" and URL `<PUBLIC_URL>/mcp`.
