# Payment Gateway

A small .NET 8 API for submitting and looking up card payments.

## Run locally

This will require the .NET 8 SDK and Docker with Compose v2.

- Start the bank simulator from this directory.

  ```sh
  docker compose up --build
  ```

- Run the gateway in another terminal.

  ```sh
  dotnet run --project src/PaymentGateway.Api
  ```

- Open [Swagger](http://localhost:5067/swagger) to try the API.
- The gateway calls `http://localhost:8080/` by default. Set `Bank__BaseUrl` to change it.

## Submit and retrieve a payment

- Send this to `POST /api/payments` with `Content-Type: application/json`. Use a future expiry date.

  ```json
  {
    "cardNumber": "2222405343240007",
    "expiryMonth": 7,
    "expiryYear": 2030,
    "currency": "GBP",
    "amount": 1050,
    "cvv": "012"
  }
  ```

- An odd last digit gets an approval. Use 2, 4, 6, or 8 for a decline and 0 for a bank outage.
- The gateway returns `201 Created` for both approvals and declines, with the payment and its `Location` header.
- Invalid payment data or malformed JSON gets `400` with validation errors and `paymentStatus: "Rejected"`. Unsupported content types get `415`.
- Bank outages, connection failures, and timeouts return `503`. Other unexpected bank responses return `502`.
- Follow `Location` or call `GET /api/payments/{id}`. An unknown ID gets `404`.
- A successful lookup returns `200 OK` with the same fields as the POST response.

  ```json
  {
    "id": "0bb07405-6d44-4b50-a14f-7ae0beff13ad",
    "status": "Authorized",
    "cardNumberLastFour": "0007",
    "expiryMonth": 7,
    "expiryYear": 2030,
    "currency": "GBP",
    "amount": 1050
  }
  ```

## Design and assumptions

I've followed the advice given in the instructions. Just to be explicit though, some assumptions made:

- Send GBP, USD, or EUR in uppercase. Use a positive 32-bit integer in minor units. For £10.50, send `1050`.
- The gateway treats a card as unexpired through the last day of its expiry month in UTC.
- Use strings for the card number and CVV to keep leading zeros. The gateway also returns the last four digits as a string.
- The gateway validates requests before calling the bank.
- The gateway keeps approved and declined payments in memory until it restarts. It stores only the last four card digits and never stores the CVV.
- The gateway gives the bank five seconds and makes a single authorization attempt per valid POST. Resending a POST can create another payment.

## Run tests

```sh
dotnet test PaymentGateway.sln
```

The tests use a stub bank, so Docker isn't used.

## Observability

Have added some prelim observability, something to expand in prod.

- The gateway logs bank decisions at Information and bank failures at Warning.
- Each entry includes the outcome, elapsed time, and trace ID.
- The gateway omits card numbers, CVVs, and HTTP bodies from its logs.

### View live metrics (optional)

- Install `dotnet-counters` in the project's `.tools` folder.

  ```sh
  dotnet tool install --tool-path ./.tools dotnet-counters --version 10.0.750501
  ```

- Leave the gateway running and start the monitor from this directory in another terminal.

  ```sh
  ./.tools/dotnet-counters monitor --name PaymentGateway.Api --counters PaymentGateway.Bank
  ```

- Try cards ending in 7, 8, and 0. Look for `payment_gateway.bank.authorization.duration` for timings in milliseconds by outcome.
