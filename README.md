

Logbook

In this section I'm going to add my thoughts about this exercise, some decisions and the why

Right now I have some time constrains so I'll try to aim more for the 4 hours time window rather than the 6 hours. I'm planning of doing in chunks during the day (1 or 2 hour chunks, we'll see). For time reasons I'll assume any question I may have and document the reasoning here.

At first glance the exercise look bit ambiguous, is not clear what info is on the dashboard on how is presented to the customer. Also the presented ticket assumes that we already have a base dashboard but we don't so that adds to the creativity part of the solver. I wont going to do the "original" dashboard and then implement the ticket over it, I'll go straight to the "good" dashboard.

The ticket raise me some questions about whats the baseline to compare and whats does "normal" means. Since we are SaaS and every customer can have a different opinion about it I would assume that the best approach is to let the customer take that decision by itself, maybe add some dashboard config so he can setup time window and % divergence 

I'll create an stophook to auto generate the AI log after every answer from claude.

So before doing anything, I'll have a session with claude to analyze the data. I'm sure that understanding the data will give me all the insights I need to understand how to craft an usable solution for this. For that I'll create a DB and restore the seed