# Meetup user guide — using the app with Postman

Welcome to Meetup, the sample app for organizing meeting groups. Everything
in this guide happens in Postman: logging in, signing up members, proposing
groups, running meetings, and handling payments — each from the perspective
of the person doing it. No terminal, no scripts, no code.

## Getting the app running

You need the app and its login server going first (in the `samples/Meetup`
folder: start Docker Desktop, run `docker compose up -d`, then start the
API with `dotnet run`, listening at `http://localhost:5123`). The rest of
this guide lives entirely in Postman.

## Opening the collection

1. In Postman, choose **Import** and pick
   `samples/Meetup/Meetup.postman_collection.json`.
2. You will see folders: **Health**, **Auth**, **Registrations**,
   **UserAccess**, **Administration**, **Payments**, **Meetings** — one per
   part of the app, in the order you will use them.
3. Check the collection's **Variables** tab: `baseUrl` should be
   `http://localhost:5123` and `authentikUrl` should be
   `http://localhost:9010`. Leave everything else alone.

Many requests remember things for you: when you register someone, their id
is saved automatically for the confirm step; proposing a group saves its id
for the accept step, and so on.

## Your accounts

Everyone logs in through the Authentik login server with one of these
accounts. What you may do depends on who you are:

| Person | Password | Role | May do |
|--------|----------|------|--------|
| `alice` | `MeetupAlice123!` | member | sign people up, browse, join, comment, pay |
| `bob` | `MeetupBob123!` | organizer | all of the above, plus propose groups and create meetings |
| `carol` | `MeetupCarol123!` | admin | confirm new members, approve or reject groups, see all lists |

## Logging in with Postman

This is the one procedure to learn — every person you play starts here.
The app never asks for your password directly; Authentik checks it and
hands Postman a **token** (your session badge), which Postman then shows
automatically on every request. Logging in takes two passes over the same
request:

**First pass — get your personal login link:**

1. Open the **Auth** folder and click **Login - run 1: get link, run 2:
   exchange code**.
2. Press **Send**. The request itself will fail — that is supposed to
   happen, because you have no code yet.
3. Open the Postman Console (**View > Show Postman Console**). At the
   bottom you will find a line starting with
   `OPEN THIS LINK IN YOUR BROWSER TO LOG IN:` followed by a long link.
   Copy that link.

**In your browser — log in and grab the code:**

4. Paste the link into your browser. The Authentik login page appears.
5. Log in as `alice` (password `MeetupAlice123!`).
6. Your browser lands on an error page saying the site can't be reached —
   **that is normal and means it worked.** Look at the address bar: it ends
   with `code=...` followed by a long code. Copy just the code part.

**Second pass — turn the code into a token:**

7. Back in Postman, open the collection's **Variables** tab, find
   `auth_code`, paste the code as its value, and save.
8. Press **Send** on the Login request again. This time it succeeds, and
   your token is stored automatically — you are now acting as alice in
   every request of the collection.

**Becoming someone else:** repeat the same procedure, logging in as `bob`
or `carol` in the browser. Each login replaces the badge, so the whole
collection now acts as that person. You will switch badges often below —
the guide always says who you should be.

> If the app ever stops recognizing you, your session has simply expired:
> log in again. Nothing is lost.

---

## As a member — joining the community

### Signing someone up

Any member can register a new person. Open **Registrations > Register
user** and press **Send** — a pre-request script invents a fresh login
(`fan` + timestamp) on every run, so repeat runs never clash with old
rows. The new account's id is saved automatically for the confirm step.

New accounts start unconfirmed. Switch to an admin badge (carol, see
"Logging in") and open **Registrations > Verify UserRegistered event →
user created, inactive**: if it is red, wait ~10 seconds and press Send
again — events travel asynchronously (see "Watching events travel
between modules" below). Green means the sign-up crossed the module
boundary into UserAccess. Then hand the account to an admin for
confirmation (see "As an admin" below).

### Browsing groups and meetings

Anyone logged in can look around. Open **Meetings > List meeting groups**
to see every group, and **Meetings > List group meetings** to see the
meetings inside one group (it uses the first group found, or the one saved
from an earlier step).

### Joining a meeting and commenting

Open **Meetings > Join meeting**. Put the meeting in `meetingId` (saved
automatically when the meeting was created) and your login in `login`.
One house rule: whoever *created* a meeting is automatically its host, so
a creator can't join their own meeting — join as a different person.

Then open **Meetings > List attendees** to see the guest list, and
**Meetings > Add comment** to join the discussion (fill in the meeting,
your login, and your text).

### Paying: subscriptions and meeting fees

Some groups charge. Open **Payments > Buy subscription** with the payer's
login, price and currency — that is the ticket to paid meetings. When a
meeting has a fee, open **Payments > Pay meeting fee** with the payer, the
meeting, and the amount.

---

## As an organizer — running groups and meetings

### Proposing a new group

Organizers suggest groups; an admin approves them. Open
**Administration > Propose meeting group**, give it a name, description,
city, country code, and the proposer's login, then press **Send**. Its id
is saved for the approval step. Ordinary members can't propose — that
action belongs to organizers.

Keep an eye on your proposals with **Administration > List proposals**.

### Creating a meeting

Once the group is approved, open **Meetings > Create meeting (organizer)**.
Pick the group, give a title and description, a start and end time in the
future (any timezone works), limits, fee, and the creator's login. Press
**Send** — the meeting id is saved, and you are automatically its host.

---

## As an admin — keeping the house in order

### Confirming new members

New sign-ups wait for you. Open **Registrations > Confirm registration
(admin)** — the saved registration id is already filled in both places it
is needed. Press **Send** and the account is live: prove it with
**Registrations > Verify UserRegistrationConfirmed event → user
active** (wait ~10 seconds first; re-send until green). Browse all
sign-ups any time with **Registrations > List registrations**. Only
admins can do any of these.

### Approving or rejecting groups

Open **Administration > Accept proposal (admin)** to turn a proposal into
a real group every member can see and join — the saved proposal id is
already filled in. Then prove the handoff with **Administration > Verify
ProposalAccepted event → group created** (wait ~10 seconds first;
re-send until green) — it also saves the new `groupId` for the Meetings
folder. Or open **Administration > Reject proposal (admin)**
instead: the proposal stays on the list, marked `Rejected`.

After accepting, open **Meetings > List meeting groups** (as anyone) to
see the new group among the rest.

### Reviewing people and money

End-of-day overview: **Payments > List subscriptions (admin)** shows every
subscription bought, and **UserAccess > List users (admin)** shows every
confirmed member. To prove the purchase reached Meetings, open
**Payments > Verify SubscriptionPurchased event → group marked paid**
(wait ~10 seconds after buying; re-send until green) — it checks that
`paymentValidUntil` was stamped on the payer's group. This only works
when the payer owns a group: the default flow buys as `ada` for the
group proposed by `ada`.

---

## Watching events travel between modules

Modules never call each other directly — they communicate through
**integration events**. Sending a command stores the change *and* the
event in the same database transaction (transactional outbox); a
background relay then delivers the event, and the receiving module
consumes it exactly once (inbox) before updating its own data. Each
**Verify … event** request in the collection checks the receiving end of
one such journey purely through the API:

| Journey | Trigger (writes) | Verify (reads) | What green proves |
|---------|------------------|----------------|-------------------|
| Sign-up | Registrations > Register user → `UserRegisteredIntegrationEvent` | Verify UserRegistered event → user created, inactive (admin) | UserAccess created the user, still inactive |
| Confirmation | Registrations > Confirm → `UserRegistrationConfirmedIntegrationEvent` | Verify UserRegistrationConfirmed event → user active (admin) | UserAccess flipped the user active |
| Group approval | Administration > Accept proposal → `MeetingGroupProposalAcceptedIntegrationEvent` | Verify ProposalAccepted event → group created | Meetings materialized the group (also saves `groupId`) |
| Subscription | Payments > Buy subscription → `SubscriptionPurchasedIntegrationEvent` | Verify SubscriptionPurchased event → group marked paid | Meetings stamped `paymentValidUntil` on the payer's groups |

Three things to know while running them:

- **They are async.** The relay polls every ~5 seconds, so after each
  trigger wait ~10 seconds, then Send the Verify request; if it is still
  red, Send again. A Verify that never turns green means the event is
  stuck, not that your token expired.
- **Use the right badge.** The two user Verify requests read
  `/api/auth/users`, which is admin-only — run them as carol. The group
  Verify requests work with any member badge.
- **Logins must line up.** The subscription check looks for a group
  created by `ada` paid for by `ada` — if you change `payerLogin` in Buy
  subscription or `proposerLogin` in Propose meeting group, change the
  other to match.

## Everything you can do — at a glance

| What | Who | Request |
|------|-----|---------|
| Sign up a user | any member | Registrations > Register user |
| Prove sign-up crossed into UserAccess (inactive) | admin | Registrations > Verify UserRegistered event |
| Confirm a user · list sign-ups | admin | Registrations > Confirm / List |
| Prove confirmation activated the user | admin | Registrations > Verify UserRegistrationConfirmed event |
| List users | admin | UserAccess > List users |
| Propose a group · list proposals | organizer (admins may list too) | Administration > Propose / List |
| Accept / reject a group | admin | Administration > Accept / Reject |
| Prove approval created the group in Meetings | any member | Administration > Verify ProposalAccepted event |
| Browse groups · browse a group's meetings | any member | Meetings > List groups / List group meetings |
| Create a meeting | organizer | Meetings > Create meeting |
| Join a meeting (not as its creator) · comment · see guest list | any member | Meetings > Join / Add comment / List attendees |
| Buy a subscription · pay a meeting fee · list subscriptions | member / admin for the list | Payments > Buy / Pay / List |
| Prove purchase marked the group paid | any member | Payments > Verify SubscriptionPurchased event |

## Good to know

- **App not responding?** Make sure Docker Desktop is running and the
  compose stack in `samples/Meetup` is up — then log in again.
- **Logged out unexpectedly?** Sessions expire. Repeat the login steps;
  your data is untouched.
- **An id needed in two places?** Confirm, accept/reject, join and comment
  ask for the id both in the address and in the body — the collection
  fills both from what it saved earlier.
- **Can't join your own meeting?** The creator is automatically the host,
  and nobody attends twice. Join as a different person.
- **Name already taken?** Old accounts stay in the database, but Register
  user invents a fresh login on every Send, so this no longer happens.
  (Proposals still use the fixed name "DDD Warsaw" — accepting twice is
  harmless: the consumer skips groups that already exist.)
- **A Verify request never turns green?** The event is stuck, not your
  token: check the API logs for relay/handler errors, confirm the right
  badge (both user Verifies need carol), and confirm matching logins
  (the subscription check needs payer = group creator).
