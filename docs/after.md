# After: Perplexity with the gateway connected

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
