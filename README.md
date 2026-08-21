Logbook

In this section I'm going to add my thoughts about this exercise, some decisions and the why

Right now I have some time constrains so I'll try to aim more for the 4 hours time window rather than the 6 hours. I'm planning of doing in chunks during the day (1 or 2 hour chunks, we'll see). For time reasons I'll assume any question I may have and document the reasoning here.

At first glance the exercise look bit ambiguous, is not clear what info is on the dashboard on how is presented to the customer. Also the presented ticket assumes that we already have a base dashboard but we don't so that adds to the creativity part of the solver. I wont going to do the "original" dashboard and then implement the ticket over it, I'll go straight to the "good" dashboard.

The ticket raise me some questions about whats the baseline to compare and whats does "normal" means. Since we are SaaS and every customer can have a different opinion about it I would assume that the best approach is to let the customer take that decision by itself, maybe add some dashboard config so he can setup time window and % divergence

I'll create an stophook to auto generate the AI log after every answer from claude. For now, besides that I dont see a benefit of adding new skills or agents

So before doing anything, I'll have a session with claude to analyze the data. I'm sure that understanding the data will give me all the insights I need to understand how to craft an usable solution for this. For that I'll create a DB and restore the seed

Insights form the session

- volume on weekends is noticeable lower that weekdays
- There are spikes that can impact the baseline
- Look like something is wrong with the timestamps since they are not in business hours. In a normal day to day we should run something to fix the data issue and work on a solution to avoid keep gathering wrong data. Looks like local time was stored as UTC but thats something dangerous to infer to for now I'm not going to generate metrics based on hours
- I'm assuming that the events are fixed or is always to be a short list

volume on weekends is noticeable lower that weekdays -> since the mean is ˜1 per day I would like to separate weekdays and weekends for the baseline comparisons

There are spikes that can impact the baseline -> we should clean that data to avoid affect the baseline, thats a business decision and I'll make the call on deleting data that goes way over the mean (P95)

Now Im going to do a planning session to define what do I want to show in the dashboard and the overall architecture of the solution

I created a requirements draft and asked claude to review it

In a real world scenario we may have separate dbs per client or any other SaaS multi tenant strategy but for the purpose of this exercise I'll leave it as it is

Some decisions I took like EF Core, ant design, Postgres DB, they work nice and fit perfect for this purpose. I choose to use an ORM because is more maintainable, easy to use an scalable than using plain SQL. Same with Postgres, its free, works nice with IANA time zones

I told claude to review my requirements draft and give me recommendations. Took some of them and modify others. ALso the schema defined from claude looks ok.

With a final requirement I created a plan.md

Before implementing the plan I downloaded some skills to make sure to follow best practices for C#, Angular and Postgres
