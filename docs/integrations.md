# Other platforms, and what changes before production

## Connecting other platforms

The core never talks to a platform directly. A platform plugs in at two seams:

- **`ICatalogSource`**: reads products, prices and schedules and maps them into the canonical
  model (`CatalogDocument`). The validator then runs on the result, so a bad sync fails the same
  way a bad hand-edited file does.
- **`ICheckoutProvider`**: turns an offer into the URL (or API call) where the buyer pays, carrying
  our enrollment reference so the sale can be attributed.

Each offer names its provider in the catalog (`"checkout": { "provider": "stripe-payment-link", … }`),
so one merchant can mix platforms. For example, classes could be sold through Mindbody and merch
through Shopify.

| Platform | Catalog source | Schedule / availability | Checkout | Payment signal back |
|---|---|---|---|---|
| **Custom site / Tilda** (this case) | curated JSON; optionally page scraping plus LLM extraction, **with merchant approval** before publish | recurring rule in the catalog | `stripe-payment-link` (implemented): Payment Link + `client_reference_id` + UTM | Stripe `checkout.session.completed` (implemented) |
| **Shopify** | Storefront/Admin GraphQL: products, variants, prices; program facts (level, coach, schedule) in metafields | metafields, or the booking app's API | Storefront `cartCreate` with cart attributes carrying the reference → `cart.checkoutUrl`; or a cart permalink | `orders/create` webhook (cart attributes, UTM landing site) |
| **WordPress + WooCommerce** | WooCommerce Store API `/wp-json/wc/store/v1/products`, or REST v3 | product meta, or a bookings plugin | `/checkout/?add-to-cart={id}` link, or Store API cart + checkout; reference in order meta | `order.created` / `order.updated` webhooks |
| **Mindbody** | Public API: programs, class descriptions, services/contracts (pricing options) | **live** class schedule and capacity from the API instead of a rule | the studio's branded web link to the pricing option (in-API sales need stored-card/PCI handling, so they come later) | Mindbody webhooks (sales, client and booking events) |
| **Fresha** | no open catalog API to rely on: services and prices from the venue's public booking page or a partner integration | Fresha booking widget | `link` provider (implemented): deep link to the service's booking page + UTM | Fresha reporting / partner integration |
| **Any custom website** | a JSON feed, the site's own API, or the curated file | rule or the site's booking API | `link` provider, or a new provider for its payment API | the site's order webhook |

What doesn't change between them: the model, search, schedule engine, tool contracts, disclosures,
funnel, JSON-LD and llms.txt. An assistant sees the same four tools whether the program lives in
Tilda + Stripe or in Mindbody.

Two refinements come with real platforms:

- **Rule vs inventory.** Victory Skating's clubs are open-capacity, so a recurring rule is the truth.
  Mindbody and booking systems know seats left. There, `AvailabilityService` should read live
  sessions from the source and fall back to the rule only when the platform has no inventory concept.
- **In-chat checkout protocols.** Where an assistant supports buying inside the chat, the
  `ICheckoutProvider` seam is where that protocol plugs in: OpenAI/Stripe's Agentic Commerce Protocol
  (ChatGPT Instant Checkout), Perplexity's merchant program / Instant Buy, and Google's AP2/UCP. The
  catalog, tools and disclosures stay as they are; only the last step changes from "here is a link"
  to "confirm and pay".

## Before production

Ordered by what I'd do first.

1. **Serve the discovery layer from the merchant's domain.** An answer engine trusts
   `victoryskating.com` more than a third-party host. Paste the generated JSON-LD into each Tilda
   page head, and point `ai.victoryskating.com` (a CNAME) at the gateway for `/llms.txt`, the fact
   sheets and `/mcp`. Also 301-redirect `/2axelclubnew` and fix the contradictory copy listed in the
   [baseline](baseline.md#site-issues-to-raise-with-the-merchant).
2. **Keep the data honest automatically.** Move the catalog into a database with a merchant-facing
   approval step. Run a scheduled job that re-reads the landing pages and the Stripe links and alerts
   on any diff (price, interval, refund text), so the AI never quotes a price the checkout no longer
   charges. Confirm the open review notes (the schedule's anchor time zone, the Axel Club coach).
3. **Checkout Sessions instead of Payment Links.** Creating a Stripe Checkout Session server-side
   gives per-enrollment metadata, expiry, idempotency keys, a prefilled customer and explicit terms
   consent, all of which Payment Links can't do. The provider interface already isolates this change.
4. **Persistence and reliability.** Store enrollments and funnel events in Postgres. Make webhook
   handling idempotent by Stripe event id, use an outbox for anything downstream, and use the Stripe SDK
   for signature checks. Webhook secrets go into a secret store.
5. **Abuse and security.** Rate-limit `/mcp` and `/api`, and cap `start_enrollment` per client (it is
   side-effect-free but writes records). Add OAuth to the MCP server once there are per-user tools
   ("my enrollments", "cancel"). Keep the "no card details in chat" rule. Skaters are often minors: the
   flow deliberately collects no personal data in the conversation; the guardian pays on Stripe.
6. **Subscription compliance.** Auto-renewal laws require clear disclosure before purchase. The
   tools already return the terms and instruct the assistant to state them; the checkout should also
   show them with a consent checkbox (point 3).
7. **Observability and AI evaluations.** Add OpenTelemetry spans per tool call (assistant, tool,
   latency, outcome) and a funnel dashboard. Rerun the baseline queries on a schedule against each
   assistant and score the answers (right price? right level? link offered?), so a regression in
   *how AI describes the product* is caught like any other regression.
8. **Multi-tenancy.** One deployment serves many merchants: per-merchant routes (`/m/{merchant}/mcp`)
   or hosts, catalogs and instructions keyed by tenant, and onboarding driven by configuration.
9. **Distribution.** Publish the server in the MCP Registry and the assistants' connector directories,
   so a user doesn't have to paste a URL. Add an MCP Apps UI (a program card with a "Join" button) for
   hosts that render one.
10. **Search at scale.** Keyword ranking is right for a few dozen programs. With hundreds of
    products, move to hybrid search (BM25 + embeddings) and multilingual synonyms.
