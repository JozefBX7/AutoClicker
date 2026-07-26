# Reliability

AutoClicker stops active input when you stop or close it. Crash recovery is optional.

## How input stops

- The global hotkey starts and stops AutoClicker from anywhere.
- The Stop button is separate from Start so generated clicks cannot immediately press it.
- Closing the window or ending the app stops the active run.
- A single-instance mutex prevents two copies from running. Starting a second copy focuses the existing window instead.
- Settings cannot be opened while the worker is active.

## GUI heartbeat

The worker also stops if the interface has not responded for five seconds. This check is lightweight and does not use a busy loop.

## Crash recovery

**Restart after an unexpected crash** is on by default. When enabled, AutoClicker starts a short-lived companion watchdog at idle priority. The watchdog waits on kernel events rather than polling, so it consumes effectively no CPU while the app is healthy.

It restarts only recognised crash exits. A normal close and a forced termination do not trigger a restart. To avoid restart loops, no more than three restart attempts are allowed in one minute.

When AutoClicker starts the crash-recovery watchdog, errors and decisions are recorded in the log.

## Timing expectations

Mouse and keyboard events use native Windows input. Delays use efficient waits instead of a CPU-spinning loop. This keeps the tool light, including beside demanding programs, but Windows scheduling, foreground applications, drivers, and system load can still cause small timing variance.

Use a sensible interval for the target application and always retain a clear stop hotkey.
