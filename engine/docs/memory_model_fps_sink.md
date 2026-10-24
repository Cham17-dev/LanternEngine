# PC memory model: FPS sinking over a session (2026-10-22)

With the Windows PC memory model on (`BB_GUEST_IN_PLACE=1`, the launcher's default since 0.9.37
beta 4) the frame rate fell over a play session: 90 at first, 75 after four minutes, ~60 after
five, 30 near the end of the player's video. Turning the memory model off fixed it.

## Evidence

Player log `lantern-20261021_204509.log` (RX 9070 XT, Ryzen 7 9700X, 2560x1440 output, scene
1708x960, 90 FPS preset). The `Load:` line comes once a minute and counts loading screens (paced at
30 FPS) too; the gameplay rate below takes their frames out.

| minute | `Load:` FPS | loads in it | gameplay FPS | moved in place, CPU-written (total) | moved to VRAM (total) |
|---|---|---|---|---|---|
| 1 | 76.9 | 2 x 3.5 s | ~83 | 96 MiB | 1 MiB |
| 2 | 77.9 | 11.9 s (respawn) | ~90 | 96 MiB | 19 MiB |
| 3 | 74.4 | 3.5 s + 11.9 s | ~90 | 534 MiB | 313 MiB |
| 4 | 74.4 | (one starts) | ~75 | 828 MiB | 547 MiB |
| 5 | 54.4 | 3.4 s + 11.9 s | ~63 | 1139 MiB | 1106 MiB |

CPU use went down as FPS did (3.8 -> 2.8 cores): the threads waited more, they did not work more.
Write faults (161-434/s) and protects (7.6k-15k pages/s) did not grow with the drop.

The same accumulation shows on this PC (RTX 5060, `logs/last_session.log`, beta 3, five minutes
with `BB_FRAME_STATS=1`): `Buffer bindings: ... in place` went 24 -> 49 -> 175 -> 200 -> 240 ->
263 MB a frame while 725 MiB of blocks moved in place as CPU-written. Stage B is the limit there
(97% busy), which hides most of the GPU side.

## Mechanism

Blocks (64 KiB) of the game's Garlic memory get a VRAM copy kept write-protected. A block the game's
code writes in 3 frames out of 60 (`NoteCpuWrite`: write faults, libc copies over watched pages)
moves in place (`ProcessDemotions`) and joins `dynamic_blocks`. Nothing ever removed a block from
`dynamic_blocks`: `VramEligible` refused it from then on, so neither `QueuePromotions` nor a later
`BindInPlace` gave it VRAM again, through unmaps and reuse of the memory too.

Most of those blocks are not per-frame data. The faulting writers are mostly `memcpy` calls of the
game's loaders and streamers (`ucrtbase` called from `+0x26adc05`, `+0x26c1776`, `+0x26bad9d`,
`+0x1fbeb62`, ...): a block filled over three frames while the GPU binds its whole vertex pool every
frame (re-protecting it after each upload) reached 3 write frames once and was then read in place
for the rest of the session. 1139 MiB moved in place for CPU writes in five minutes, ~18 000 blocks,
which is about all of the game's GPU data the scene uses (1164 MiB first bound to VRAM). Each such
block holding geometry or buffers the scene reads every frame is read over PCIe: the RTX 5060
reads imported host memory at 10.5 GB/s (`tools/host_import_probe_win.cpp`), so every 10 MB
read a frame costs about a millisecond of GPU time, and vertex fetch over PCIe stalls far worse
than its bandwidth suggests. The share of the frame's data read in place only grows with play
(deaths and area loads put new assets into memory already marked), so the frame rate sinks.

Second, smaller: the freed VRAM slots (over 128 MiB of them almost always, after so many blocks
left) start an evacuation every 10 s (`ProcessIdleBlocks`), which moved every block of the
sparsest 64 MiB chunk in place, also the ones the GPU was reading that frame. Those came back at
their next binding (`group_demoted` -> `QueuePromotions`): two sparse binds each, each bind waiting
for the previous submission to finish on the GPU, and read over PCIe in between. That is most of
the 1106 MiB "moved to VRAM" in the table.

## Fix (switches)

- `BB_DYNAMIC_RETRY=<seconds>` (default 10; 0 = as before): a block moved in place for CPU writes
  gets a VRAM copy again once its wait is over and the GPU bound its 2 MiB in the last second
  (`ProcessDynamicRetries`, once a second). It is write-protected again; if the game's code still
  writes it in 3 frames of 60 it moves back and waits twice as long (up to 32x). A block that
  stayed in VRAM four times its wait starts over. At most `BB_DYNAMIC_RETRY_MB` (default 8) MiB a
  second is retried, none while VRAM is tight. The new copy is uploaded whole from the game's
  memory before it is read (`ProcessRetryFills`): its slot may hold another block's bytes.
- `BB_EVACUATE_IN_USE=1` restores evacuations of blocks the GPU read in the last 10 s while VRAM
  has room (default 0: those stay, the idle ones of the chunk go).

## Promoted blocks with another block's bytes (device lost, RTX 2070 report)

A beta 4 report (RTX 2070 8 GB, 16 GB RAM) lost the device after "loaded blocks moved from in
place to VRAM" climbed to 271 MiB. The GPU finished `cs 4ca76892` (1x1x1) and never started the
indirect `cs 2da7fe60` after it. This is the particle pair of the 2026-10-06 hang, whose loop counts
come from memory. `QueuePromotions` takes a freed VRAM slot (`AllocateResidency`) without clearing
it and only marks the block CPU-modified, so only the ranges later bindings name were uploaded.
The rest of the block kept the slot's previous block's bytes, and shaders reading through the
page table or vertex fetch past a binding read them. `EnsureResident` clears reused slots for
exactly that reason; promotions did not. Now every promoted block is uploaded whole from the game's
memory before anything reads it (`ProcessPromoteFills`). Not proven to be that crash: the same log
had 593 MiB of commit left with 2816 MiB of the game's memory imported (pinned) for the GPU,
which can also end in a lost device.

## Diagnostics

Once a minute with the PC memory model (`ReportResidency`):

    Guest memory, last 60 s: N MiB moved to VRAM (loaded, GPU-written, back after idle, back after
    an evacuation, CPU-written retried); N MiB moved in place (CPU-written, handed out again, idle,
    evacuated, copied back); sparse binds before N submissions (N waited for the GPU, N ranges),
    N ms in vkQueueBindSparse (longest); now N MiB in VRAM copies, N MiB in place (N MiB
    CPU-written); bound in place N MB a frame (N MB in CPU-written blocks)

## A/B on this PC

Not run yet (2026-10-22: the user postponed game runs). Plan: two sessions of 12+ minutes with
the same settings, `BB_DYNAMIC_RETRY=0 BB_EVACUATE_IN_USE=1` (as before) against the defaults,
comparing the per-minute `Load:` FPS and the `Guest memory, last 60 s` lines. The fix holds if
"now ... MiB in place (... CPU-written)" and "bound in place ... MB a frame" stay flat instead of
climbing, and the FPS with them.
