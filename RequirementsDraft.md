#### Background:

- take a look fo @docs/relay-data-audit.md thats the base data that I have
- Each account can have multiple locations
- Weeks start on Monday and finish on Sunday

#### What I want:

- Using that data I need to create a dashboard to show the data per account

#### Data to show:

- Outcomes per event type

#### How to show it:

- At the top show some filters:
  - multiselect per location: can show the data for all the locations or only some of the locations
  - comparison window: number of weeks to compare. 1 is compare against last week. Max number is the max of weeks available on the data for that account
  - %tolerance: % of divergence from the mean to show the data as an issue (red)
  - view week: by default to current week but allow the user to go back in time and see metrics from previous weeks
- Each event type will be a section of the dashboard
- Inside the event type section show the metrics for the last week or whatever is configure on the time window, one metric per outcome type
- show the outcome type metric with the number from left to the right of the screen
- Below each number show a graph with a time window of the comparison used to calculate the baseline
- Each outcome will categorized as good or bad outcome:
  - if is a bad outcome and is over the tolerance window it will show on red
  - if is a good outcome and is under the tolerance window it will show on red
  - if an outcome is very close to the red line (last 20% of the defined tolerance) it will show on orange
  - Otherwise will show on green

#### Architecture

- .NET 8+ (C#) on the backend
  - EF Core as the ORM - favor aggregations to generate the metrics
- Angular on the frontend
  - Use Router query params to select the account and locations
  - ant design for the ui
- Postgres DB - use the one we already have
