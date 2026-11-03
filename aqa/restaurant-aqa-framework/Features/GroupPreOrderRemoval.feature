@level3
Feature: Removing a guest from a group pre-order

  The owner of a group booking can remove a guest, then see who is left and the new total.
  Only a guest of this group can be removed, and only by the owner. The total on the page
  counts carts the guests have already submitted. A card shows that guest's own dishes
  whether or not the cart was submitted. After the last guest with dishes is removed, the
  page says that nobody has added dishes yet and the total is zero.

  Rule: The owner can remove a guest and the totals follow

    @ui
    Scenario: The owner removes one guest who is still choosing
      Given a group pre-order with these guests
        | guest | dish   | quantity |
        | Ada   | first  | 1        |
        | Ada   | second | 1        |
        | Bea   | second | 2        |
      When the owner reviews the group pre-order
      And the owner removes "Ada"
      Then the owner is still listed
      And "Bea" is still in the group with the same dishes
      And "Ada" is no longer in the group
      And the group total is unchanged

    @ui
    Scenario: The owner removes a guest who already submitted a cart
      Given a group pre-order with these guests
        | guest | dish   | quantity | submitted |
        | Ada   | first  | 1        | yes       |
        | Bea   | second | 2        | no        |
      When the owner reviews the group pre-order
      And the owner removes "Ada"
      Then the owner is still listed
      And "Bea" is still in the group with the same dishes
      And "Ada" is no longer in the group
      And the group total no longer includes "Ada"

    @ui
    Scenario: The owner removes the last guest
      Given a group pre-order with these guests
        | guest | dish  | quantity |
        | Ada   | first | 1        |
      When the owner reviews the group pre-order
      And the owner removes "Ada"
      Then "Ada" is no longer in the group
      And nobody has added any dishes yet
      And the group total is zero

    @ui @smoke
    Scenario: The waiter sees the order without the guest the owner removed
      Given a group pre-order with these guests
        | guest | dish   | quantity | submitted |
        | Ada   | first  | 1        | yes       |
        | Ada   | second | 2        | yes       |
        | Bea   | second | 3        | yes       |
      When the owner reviews the group pre-order
      Then the owner sees every guest's dishes
      When the owner removes "Bea"
      And the owner sends the pre-order to the restaurant
      And the waiter opens that pre-order
      Then the waiter sees only the dishes that were left

  Rule: Only the owner can remove a guest of this group

    @api @negative
    Scenario: A guest of another group cannot be removed from this one
      Given a group pre-order with these guests
        | guest | dish  | quantity |
        | Ada   | first | 1        |
      And a separate group pre-order with a guest called "Cara"
      When the owner removes a guest of the separate group
      Then the visitor is told "Guest session does not belong to this group reservation."

    @api @negative
    Scenario: A guest who does not exist cannot be removed
      Given a group pre-order with these guests
        | guest | dish  | quantity |
        | Ada   | first | 1        |
      When the owner tries to remove a guest who does not exist
      Then the owner is told the guest could not be found

    @api @negative
    Scenario: A group that does not exist cannot lose a guest
      Given a group pre-order with these guests
        | guest | dish  | quantity |
        | Ada   | first | 1        |
      When the owner tries to remove a guest from a group that does not exist
      Then the visitor is told "Group reservation was not found"

    @api @negative
    Scenario: Another customer cannot remove a guest
      Given a group pre-order with these guests
        | guest | dish  | quantity |
        | Ada   | first | 1        |
      When another customer tries to remove "Ada"
      Then the visitor is told "Only the reservation owner can perform this action."

    @api @negative
    Scenario: The waiter cannot remove a guest
      Given a group pre-order with these guests
        | guest | dish  | quantity |
        | Ada   | first | 1        |
      When the waiter tries to remove "Ada"
      Then the visitor is told "Only the reservation owner can perform this action."

    @api @negative
    Scenario: A visitor who is not signed in cannot remove a guest
      Given a group pre-order with these guests
        | guest | dish  | quantity |
        | Ada   | first | 1        |
      When a visitor who is not signed in tries to remove "Ada"
      Then the visitor must sign in
