# Solution

**Direction 1. Approach: BDD + Screenplay.** Level 3 only: the owner removes a guest from a group pre-order. Levels 1 and 2 are not in this suite.

## Configure and run

From `aqa/restaurant-aqa-framework`, copy `.env.example` to `.env` and fill in `UI_URL`, `API_URL`, `WAITER_USERNAME`, and `WAITER_PASSWORD`. Then:

```bash
dotnet build
dotnet run --no-build -- --report-trx
```

## Run a subset

Tags are TUnit categories. Level 3 is `@level3`. These scenarios also use `@ui`, `@api`, `@smoke`, and `@negative`.

```bash
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=level3]" --report-trx
```

Replace `level3` with another tag to run that subset.

## Reporting and the pipeline

Report Portal runs locally in Docker. GitHub Actions is the cloud pipeline. This repository does not stand up a Portal in the cloud.

**Local Portal.** Start Docker Desktop. From `aqa/restaurant-aqa-framework/reportportal`:

```bash
docker compose up -d
```

Open `http://localhost:8080`. The default login is `superadmin` and the default password is `erebus`, unless you set `RP_INITIAL_ADMIN_PASSWORD` before the first start. That account already has a personal project named `superadmin_personal`. Under the user profile, create an API key and copy the secret at the moment it is shown. Put these in `.env` and run the tests again:

```bash
RP_URL=http://localhost:8080
RP_PROJECT=superadmin_personal
RP_API_KEY=
```

Leave any of them empty and the agent stays off. A value already set in the shell wins over `.env`, so clear a stale `RP_API_KEY` there before the next run. A UUID copied from the key list, or the placeholder text, is rejected and the launches page stays empty. The launch is named Restaurant AQA. Its header can say Failed while every scenario passed, because the feature row is closed as skipped. Passing steps have no log lines.

## Behaviour the homework leaves open

The tests follow the deployed API and group page. Repeat submit is level 2 and is out of scope.

- Another group's guest: 400, `Guest session does not belong to this group reservation.`
- Unknown guest: 404. Unknown group: 404, `Group reservation was not found`.
- Not the owner (another customer, or the waiter): 403, `Only the reservation owner can perform this action.`
- No token: 401 whose message contains `Unauthorized`.
- The grand total counts submitted carts only. A card subtotal is that person's own dishes. Removing a guest who is still choosing does not change the grand total.
- Removing the last guest succeeds. With no dishes left, the page says nobody has added dishes yet and the total is 0. `participants: []` and `total: 0` is the API result only when the page was never opened, so no session rows exist.
