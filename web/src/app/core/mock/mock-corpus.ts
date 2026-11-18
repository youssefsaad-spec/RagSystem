import { DocumentViewDto } from './backend-dtos';

/** A document the mock backend starts with. Its chunks are short excerpts written from the real V2 and V3 documentation. */
export interface SeedDocument {
  version: 'v2' | 'v3';
  fileName: string;
  title: string;
  status: DocumentViewDto['status'];
  error?: string;
  chunks: string[];
}

const flowStepsSeats = [
  '## Workflow: booking with seats',
  '',
  '| Step | API | What to do |',
  '|---|---|---|',
  '| 1 | GetAvailableOffers | Search for flight offers. Each offer has its own offerId. |',
  '| 2 | FareConfirmOffer | Validate the fare of the chosen offer. Returns a new offerId. |',
  '| 3 | AddPassengersDetails | Add the details of every passenger. Returns a new offerId. |',
  '| 4 | SeatMap | Show the seat map of each flight segment. Returns a new offerId. |',
  '| 5 | FareCheck | Send the selected seats by cell code and revalidate the price. |',
  '| 6 | Hold / Book | Use the offerId from the FareCheck response. Call Hold or BookAndPay. This generates an airline PNR and an ndcBookingReference. |',
].join('\n');

/** Nearly the same text as {@link flowStepsSeats}, as in the real guide where the last steps repeat in every scenario. */
const flowStepsBundles = [
  '## Workflow: booking with bundles',
  '',
  '| Step | API | What to do |',
  '|---|---|---|',
  '| 1 | GetAvailableOffers | Search for flight offers. Each offer has its own offerId. |',
  '| 2 | FareConfirmOffer | Validate the fare of the chosen offer. Returns a new offerId. |',
  '| 3 | GetOfferAvailableBundles | Show the bundles of the offer. |',
  '| 4 | AddPassengersDetails | Add the details of every passenger. Returns a new offerId. |',
  '| 5 | FareCheck | Send the selected bundles and revalidate the price. |',
  '| 6 | Hold / Book | Use the offerId from the FareCheck response. Call Hold or BookAndPay. This generates an airline PNR and an ndcBookingReference. |',
].join('\n');

export const SEED_DOCUMENTS: SeedDocument[] = [
  {
    version: 'v3',
    fileName: 'ndc-portal-apis-v3.md',
    title: 'NDC Portal APIs V3',
    status: 'Ready',
    chunks: [
      [
        '# NDC Portal APIs V3',
        '',
        '## Chapter 1. Business-API Process',
        '',
        'Before starting to use the APIs, it is important to know the correlation between the business workflow and the backend API flow of the flight booking process, starting from flight search to the final booking (payment).',
        '',
        'The NDC Portal APIs support these operations: Flight Search, Fare Confirmation, Add Passengers Details, Retrieve Available Bundles, Retrieve Available Ancillaries, Retrieve Seat Map and Fare Check.',
      ].join('\n'),
      [
        '## Document History',
        '',
        '| Date | Version | Changes |',
        '|---|---|---|',
        '| 28/6/2026 | V2 | Adding V2 Features |',
        '| 10/9/2026 | V2.1 | Adding Enums of the system; return Offer Provider and selector; temporarily disable Void functionality |',
        '| 23/10/2026 | V3 | Adding V3 Features: seat selection by cell code, GetOfferAvailableBundles and SeatMap return an offerId, seats and EMD tickets in the order responses, ticket status per passenger |',
      ].join('\n'),
      [
        '## Chapter 2. API Access & Usage',
        '',
        '### Authentication',
        '',
        'Every request must carry your subscription key. Send it in the header named "**Ocp-Apim-Subscription-Key**". A request without a valid key is refused with 401.',
        '',
        'Use the sandbox key while testing and the production key once your integration is approved.',
      ].join('\n'),
      [
        '### Rate Limits',
        '',
        '| Environment | Limit | Notes |',
        '|---|---|---|',
        '| Sandbox (Testing) | Default limits apply | Standard rate limiting is enforced by Azure API Manager. |',
        '| Production | Agreed per agency | Set when your subscription is created. |',
        '',
        'If an application exceeds the allowed request limit, the API enforces the rate limits by returning the error code: "429 - Too Many Requests".',
      ].join('\n'),
      [
        '## Chapter 3. Offer ID V3 Flow',
        '',
        'The V2 rule still applies: always send the latest offerId. Each new offerId replaces the previous one, and an old one is refused with 409 Conflict.',
        '',
        '| Call | Send the offerId from | Returns an offerId |',
        '|---|---|---|',
        '| FareConfirmOffer | GetAvailableOffers | Yes, a new one |',
        '| GetOfferAvailableBundles | FareConfirmOffer | Yes, the same one you sent (new in V3) |',
        '| SeatMap | GetAncillary, or AddPassengersDetails | Yes, a new one (new in V3) |',
        '| Hold / BookAndPay | FareCheck if it was called; otherwise AddPassengersDetails | No |',
      ].join('\n'),
      flowStepsSeats,
      flowStepsBundles,
      [
        '### FareCheck',
        '',
        '`POST /api/V3/Order/FareCheck`',
        '',
        'This endpoint submits the selected bundles, ancillaries and seats, and revalidates their prices before Hold or BookAndPay. It returns the updated passenger details, the selected ancillaries and seats with their prices, and a new offerId.',
        '',
        'FareCheck is called only when bundles, ancillaries or seats are selected. If none are selected, skip FareCheck and call Hold or BookAndPay with the offerId from the last call.',
      ].join('\n'),
      [
        '### Hold and BookAndPay',
        '',
        'Hold reserves the booking without payment; BookAndPay books and pays in one call. Both return the **ndcBookingReference**, which you use for FareConfirmAfterHold, BookAfterHold, CancelAfterHold, Retrieve and VoidTicket.',
        '',
        'The response includes the selected seats of each passenger; BookAndPay also returns the ticket and EMD tickets of each passenger.',
      ].join('\n'),
      [
        '## Chapter 6. Response Codes & Error Handling',
        '',
        '##### SeatMap, Hold and BookAndPay Endpoints (409)',
        '',
        '| Message | Code |',
        '|---|---|',
        '| Hold, BookAndPay: Please call FareCheck to confirm your bundle, ancillary and seat selections before booking. | VR_72 |',
        '',
        '| 429 Too Many Requests | The rate limit is exceeded. |',
      ].join('\n'),
      [
        '## Chapter 7. FAQs',
        '',
        '**What is the difference between Hold and BookAndPay?** Hold reserves the seats and fare so the passenger can pay later; call FareConfirmAfterHold to verify the price after holding. BookAndPay completes the booking and the payment in one step. Call FareCheck before either one to revalidate the price after your selections.',
      ].join('\n'),
    ],
  },
  {
    version: 'v3',
    fileName: 'whats-new-v3.md',
    title: "NDC Portal APIs V3: What's New",
    status: 'Ready',
    chunks: [
      [
        '## 1. At a glance',
        '',
        '| Area | V2 | V3 |',
        '|---|---|---|',
        '| Base path | `/api/V2/` | `/api/V3/` |',
        '| Authentication and rate limits | `Ocp-Apim-Subscription-Key` | No change |',
        '| Seat selection | Seats are sent to FareCheck as `Seat` items in `passengersSelectedAncillaries` | Seats are sent to FareCheck in their own field, `passengersSelectedSeats`, by seat number (`cellCode`) |',
        '| SeatMap | Does not return an `offerId` | Returns a new `offerId` to use in the next call |',
        '| Booking responses | Fares and ancillaries | Also seats, seat prices, ticket status and EMD tickets for each passenger |',
      ].join('\n'),
      [
        '## 2. The booking flow',
        '',
        '**Flow:** GetAvailableOffers → FareConfirmOffer → [GetOfferAvailableBundles] → AddPassengersDetails → [GetAncillary] → [SeatMap] → [FareCheck] → Hold or BookAndPay',
        '',
        'GetOfferAvailableBundles, GetAncillary and SeatMap only show options and prices. Nothing is chosen until FareCheck. FareCheck is needed only when bundles, ancillaries or seats are chosen.',
      ].join('\n'),
      [
        '## 3. Which offerId to send',
        '',
        'Always send the latest offerId. Each new offerId replaces the previous one, and an old one is refused with 409 Conflict. If you skip a step, send the offerId from the last step you called. SeatMap and GetOfferAvailableBundles now return an offerId (new in V3).',
      ].join('\n'),
      [
        '### SeatMap',
        '',
        '`POST /api/V3/Offers/SeatMap`',
        '',
        '| Change | Field | Description |',
        '|---|---|---|',
        '| + | `offerId` | The new offerId. Send it to FareCheck. |',
        '| + | `seatCellsDetail[].cellCode` | The seat number (for example "12A"). Use it to choose the seat in FareCheck. |',
        '| − | `seatCellsDetail[].code` | Removed. Use `cellCode`. |',
      ].join('\n'),
      [
        '## 6. Moving from V2 to V3: checklist',
        '',
        '1. Change the base path from `/api/V2/` to `/api/V3/`.',
        '2. Send seats to FareCheck in `passengersSelectedSeats` using `cellCode`.',
        '3. Use the offerId returned by SeatMap and GetOfferAvailableBundles in your next call.',
        '4. Read the seats, ticket status and EMD tickets in the Hold, BookAndPay and Retrieve responses.',
      ].join('\n'),
    ],
  },
  {
    version: 'v3',
    fileName: 'OffersV3.swagger.json',
    title: 'NDC Portal Offers API V3',
    status: 'Failed',
    error: 'Interrupted by a restart before processing finished. Upload the file again.',
    chunks: [],
  },
  {
    version: 'v2',
    fileName: 'ndc-portal-apis-v2.md',
    title: 'NDC Portal APIs V2',
    status: 'Ready',
    chunks: [
      [
        '# NDC Portal APIs V2',
        '',
        '## Chapter 1. Business-API Process',
        '',
        'The NDC Portal APIs support these operations of the flight booking process: Flight Search, Fare Confirmation, Add Passengers Details, Retrieve Available Bundles, Retrieve Available Ancillaries, Retrieve Seat Map and Fare Check.',
      ].join('\n'),
      [
        '## Chapter 2. API Access & Usage',
        '',
        '### Authentication',
        '',
        'Every request must carry your subscription key in the header named "**Ocp-Apim-Subscription-Key**". A request without a valid key is refused with 401.',
      ].join('\n'),
      [
        '### Rate Limits',
        '',
        'Standard rate limiting is enforced by Azure API Manager. If an application exceeds the allowed request limit, the API returns the error code "429 - Too Many Requests".',
      ].join('\n'),
      [
        '## Chapter 3. Offer ID Flow',
        '',
        'Always send the latest offerId. Each new offerId replaces the previous one, and an old one is refused with 409 Conflict. In V2, SeatMap does not return an offerId.',
      ].join('\n'),
      [
        '### Hold and BookAndPay',
        '',
        'Hold reserves the booking without payment; BookAndPay books and pays in one call. Both return the ndcBookingReference, used by FareConfirmAfterHold, BookAfterHold, CancelAfterHold, Retrieve and VoidTicket.',
      ].join('\n'),
      [
        '## Chapter 6. Response Codes & Error Handling',
        '',
        '| 409 Conflict | The offerId is old: send the latest one. |',
        '| 429 Too Many Requests | The rate limit is exceeded. |',
      ].join('\n'),
    ],
  },
];
