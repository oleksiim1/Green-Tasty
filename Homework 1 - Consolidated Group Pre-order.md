# View the consolidated group pre-order
Green Tasty user story for the reservation owner’s consolidated group pre-order screen.

**Epic:** Order Management  
**Name:** View the consolidated group pre-order  
**Role:** Reservation Owner
## User Story
As a reservation owner, I want to see every participant’s pre-order on one screen, so that I can check each person’s choices and the table total before the visit.
## Preconditions
- The reservation owner is logged in.
- The reservation status is Reserved, and a group session already exists for it.
- Participants on this screen are the owner and guests who opened the link and entered a name. Each person’s dishes are stored in their personal subcart.
## Acceptance Criteria
### AC1. All pre-orders on one screen
**Given** the reservation owner opens the consolidated pre-order screen for their active reservation  
**When** at least one participant has added dishes to a subcart  
**Then** one screen shows each participant with their name  
**And** under each participant their items are listed: name, quantity, unit price, and line total  
**And** the owner’s own pre-order is included in the same list.
### AC2. A participant has not chosen anything yet
**Given** a guest has joined the session but has not added any dishes  
**When** the reservation owner opens the screen  
**Then** that guest appears in the list with the state “No dishes selected yet”  
**And** that participant’s subtotal is 0.
### AC3. Totals are calculated automatically
**Given** participants’ subcarts contain dishes with prices  
**When** the reservation owner opens or refreshes the screen  
**Then** each participant has a subtotal equal to the sum of their line items  
**And** one table total is shown, equal to the sum of all participant subtotals  
**And** both totals match the subcart data at the moment the screen is opened or refreshed.
### AC4. The screen is available only to the owner of an active reservation
**Given** the user is not the owner of this reservation, or the reservation status is no longer Reserved  
**When** they open this screen  
**Then** the consolidated pre-order is unavailable to them.
## Assumptions
- The screen only displays pre-orders. Other people’s items cannot be changed from it.
- The list includes only people who joined: the system does not know people who received the link and never opened it.
- The total is the sum of dish prices. Taxes and service charges are not included.
- The data is current at the moment the screen is opened or manually refreshed. Near-real-time updates are a separate backlog item.
## Open questions
- If a dish becomes unavailable (“On Stop”) after it was added, does it stay in the consolidated view, and how is it marked?
- Does this screen show only name, quantity, and price, or also the photo, serving size, and category?
- Is the price in the total the price at the time the dish was added, or the current menu price?
