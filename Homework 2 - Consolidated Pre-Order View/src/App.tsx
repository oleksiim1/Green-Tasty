type PreOrderItem = {
  id: string
  name: string
  quantity: number
  unitPrice: number
  image: string
  imagePosition?: string
}

type Participant = {
  id: string
  name: string
  role?: "owner"
  items: PreOrderItem[]
}

const participants: Participant[] = [
  {
    id: "owner",
    name: "You",
    role: "owner",
    items: [
      {
        id: "strawberry-salad",
        name: "Fresh Strawberry Mint Salad",
        quantity: 1,
        unitPrice: 17,
        image: "/assets/e60d7.png",
        imagePosition: "50% 62%",
      },
      {
        id: "lentil-salad",
        name: "Roasted Sweet Potato & Lentil Salad",
        quantity: 1,
        unitPrice: 10,
        image: "/assets/f73c6.png",
        imagePosition: "50% 54%",
      },
    ],
  },
  {
    id: "maya",
    name: "Maya",
    items: [
      {
        id: "maya-lentil-salad",
        name: "Roasted Sweet Potato & Lentil Salad",
        quantity: 2,
        unitPrice: 10,
        image: "/assets/f73c6.png",
        imagePosition: "50% 54%",
      },
    ],
  },
  {
    id: "daniel",
    name: "Daniel",
    items: [],
  },
]

const money = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  maximumFractionDigits: 0,
})

const participantSubtotal = (participant: Participant) =>
  participant.items.reduce(
    (total, item) => total + item.quantity * item.unitPrice,
    0,
  )

function AccessDenied({ reason }: { reason: "owner" | "status" }) {
  return (
    <main className="access-page">
      <section className="access-card" aria-labelledby="access-title">
        <div className="access-mark" aria-hidden="true">
          !
        </div>
        <p className="eyebrow">Reservation access</p>
        <h1 id="access-title">This pre-order isn’t available</h1>
        <p>
          {reason === "owner"
            ? "Only the reservation owner can review the complete group pre-order."
            : "Group pre-orders can only be viewed while the reservation is active."}
        </p>
        <button type="button" onClick={() => window.history.back()}>
          Back to reservation
        </button>
      </section>
    </main>
  )
}

export default function App() {
  const previewState = new URLSearchParams(window.location.search).get(
    "preview",
  )
  const isReservationOwner = previewState !== "guest"
  const reservationStatus = previewState === "closed" ? "Completed" : "Reserved"

  if (!isReservationOwner) {
    return <AccessDenied reason="owner" />
  }

  if (reservationStatus !== "Reserved") {
    return <AccessDenied reason="status" />
  }

  const tableTotal = participants.reduce(
    (total, participant) => total + participantSubtotal(participant),
    0,
  )

  return (
    <main className="preorder-page">
      <header className="topbar">
        <div className="topbar-copy">
          <p className="eyebrow">Saturday, May 24 · 7:30 PM</p>
          <h1>Group Pre-Order</h1>
        </div>
        <button
          className="icon-button"
          type="button"
          aria-label="Close group pre-order"
          onClick={() => window.history.back()}
        >
          <img src="/assets/a5181.svg" alt="" />
        </button>
      </header>

      <section className="summary-strip" aria-label="Reservation summary">
        <div>
          <span>Reservation</span>
          <strong>Garden Table · 4 guests</strong>
        </div>
        <div className="progress-copy">
          <span>Pre-orders</span>
          <strong>2 of 3 selected</strong>
        </div>
      </section>

      <section className="participants" aria-labelledby="participants-title">
        <div className="section-heading">
          <div>
            <p className="eyebrow">At your table</p>
            <h2 id="participants-title">Everyone’s selections</h2>
          </div>
          <span className="participant-count">
            {participants.length} people
          </span>
        </div>

        <div className="participant-list">
          {participants.map((participant) => {
            const subtotal = participantSubtotal(participant)

            return (
              <article className="participant-card" key={participant.id}>
                <header className="participant-header">
                  <div className="participant-identity">
                    <span className="avatar" aria-hidden="true">
                      {participant.name.slice(0, 1)}
                    </span>
                    <div>
                      <div className="participant-name-row">
                        <h3>{participant.name}</h3>
                        {participant.role === "owner" && (
                          <span className="owner-badge">Reservation owner</span>
                        )}
                      </div>
                      <p>
                        {participant.items.length
                          ? `${participant.items.length} ${
                              participant.items.length === 1 ? "dish" : "dishes"
                            } selected`
                          : "Waiting for a selection"}
                      </p>
                    </div>
                  </div>
                  <div className="subtotal">
                    <span>Subtotal</span>
                    <strong>{money.format(subtotal)}</strong>
                  </div>
                </header>

                {participant.items.length > 0 ? (
                  <div className="item-list">
                    {participant.items.map((item) => {
                      const lineTotal = item.quantity * item.unitPrice

                      return (
                        <div className="order-item" key={item.id}>
                          <img
                            className="dish-image"
                            src={item.image}
                            alt=""
                            style={{ objectPosition: item.imagePosition }}
                          />
                          <div className="item-copy">
                            <h4>{item.name}</h4>
                            <p>Expected ready time · 10 minutes</p>
                            <div className="item-meta">
                              <span>
                                Qty <strong>{item.quantity}</strong>
                              </span>
                              <span>{money.format(item.unitPrice)} each</span>
                            </div>
                          </div>
                          <strong className="line-total">
                            {money.format(lineTotal)}
                          </strong>
                        </div>
                      )
                    })}
                  </div>
                ) : (
                  <div className="empty-selection">
                    <span className="empty-icon" aria-hidden="true">
                      <i />
                      <i />
                      <i />
                    </span>
                    <div>
                      <strong>No dishes selected yet</strong>
                      <p>
                        Their choices will appear here once they add a dish.
                      </p>
                    </div>
                  </div>
                )}
              </article>
            )
          })}
        </div>
      </section>

      <footer className="total-footer">
        <div className="total-copy">
          <div>
            <span>Table total</span>
            <p>Taxes and gratuity are not included</p>
          </div>
          <strong>{money.format(tableTotal)}</strong>
        </div>
        <button type="button" onClick={() => window.history.back()}>
          Done reviewing
        </button>
      </footer>
    </main>
  )
}
