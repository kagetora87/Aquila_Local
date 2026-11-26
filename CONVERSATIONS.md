# Aquila Local 1.5 — chat deletion and context compaction

## Delete a chat

Right-click a conversation and choose **Delete chat…**, or select a conversation in the sidebar and press **Delete**. Both paths open the same confirmation with **No** selected by default. Right-click selects the row under the pointer first. Delete is scoped to the chat list; editing text in the composer retains ordinary text deletion.

Confirming removes that conversation's saved JSON (including its attachment snapshots and any compacted-context checkpoint) and a leftover temporary snapshot. A neighboring chat becomes selected. Deleting the last chat opens a fresh blank conversation. Cancellation leaves the list and file unchanged; a filesystem error keeps the row and displays the error. Deletion is blocked during model work and attachment processing.

Learned memories remain in the separate memory catalog, including supporting quotes. The confirmation explicitly explains this. Use **Memories** to forget them. Existing exports, backups and any original attached files remain. There is no in-app undo for deleting a chat. The updater does not delete user chats; deletion tests use synthetic profiles only.

`Storage.DeleteChat` accepts only a GUID in the application's N-format ID scheme before constructing the local filename; arbitrary paths are rejected. It performs no recursive deletion. `MainWindow.Conversations.cs` owns one routed command used by both inputs and manages selection after success. The command is unavailable while busy, attaching or closing.

## Compact a session's context

Click **Compact context…** near Settings. It becomes available after at least five complete turns remain eligible for the model's input. Confirm to summarize an earlier prefix using the selected local model. The four most recent complete turns stay verbatim. All original messages and attachment snapshots remain visible, saved and exportable. Compaction is manual; it does not silently scan other chats or change automatic memory.

The next model request receives the summary as labeled reference data, followed by unsummarized recent conversation and the new question. Old summarized turns are excluded from that request. This frees input space; it does not increase the model's context limit, reduce allocated model/VRAM memory, or reduce cumulative session usage.

**View summary…** shows the generated text, date, model and covered-turn count. **Use original history** disables that checkpoint, with confirmation. Original history is then eligible again under the ordinary limits. Aquila keeps the most recent whole turns that fit after reserving space for the reply. Reset does not reset token totals. Markdown exports include the checkpoint followed by the full original transcript.

Compaction is lossy and model-dependent. Review important facts, commitments and exact identifiers. Text attachments are included in the summarizer's transcript; raw image pixels, hidden reasoning and tool traces are not. It can preserve recorded descriptions of an image, but cannot reconstruct omitted pixels. Recent images remain intact. Saved personal/project memories and user-maintained notes are separate and are still handled normally for future answers.

## Bounded operation and failure handling

`ChatCompactor` merges the previous summary with a contiguous prefix of unsummarized complete turns. It retains four recent turns, considers at most 32 older turns per operation, bounds the serialized planning prompt to one million characters, and uses exact engine token counts to find a prefix fitting input plus response allowance plus a 64-token margin. It never skips over an oversized earliest turn. If none fit, it explains that context should be increased or a new chat started.

Each click performs at most one generation request. It respects the saved model, CPU thread count, GPU offload and thinking settings; it requests at most 2,048 output tokens (further bounded by half the context). It supplies neutral summarization instructions instead of a personality profile or tools, and does not extract long-term memories. CPU runs have an eight-minute deadline; GPU runs have a three-minute deadline. Stop cancels the operation. Status distinguishes loading, measuring the source and writing a summary. Avatar Thinking/Idle also follow this operation if the avatar is enabled.

A successful response must end normally, contain bounded nonempty final text without raw thinking markup, and have a smaller token-counted summary representation than its source. The diagnostic counts compare the same summarization envelope before/after; they are not the entire live conversation occupancy or a promised saving for every future query. If more than four complete turns remain after a partial prefix, the notice explains that another compaction can continue the work.

Failure, timeout, cancellation, an unfinished response, or a summary that is not smaller leaves the previous checkpoint in use. The new checkpoint is committed only after the model finishes and validation succeeds. The UI rolls it back if saving fails. Full original turns are never rewritten by the summarizer. A missing/invalid saved anchor falls back to original history rather than silently discarding messages.

`ChatSession.CompactionPromptTokens` and `CompactionCompletionTokens` add summarization overhead to cumulative session totals exactly once, including failed generations whose usage was returned. An interrupted inference without returned counts flags totals partial. Preflight failure and cancellation before inference do not add guessed counts. Hover over session usage for compaction overhead. This overhead persists independently of the current checkpoint, so resetting/replacing a summary does not erase accounting.

## Verification

Run `Aquila Local.exe --verify-conversations <fresh-evidence> <Deckard-model.gguf>` with the delivered engine/runtime assets. The suite uses synthetic profiles and one bounded GPU model with two CPU threads, 8,192 context and thinking enabled. Its 22 checks cover targeted deletion/temp cleanup, adjacent files, traversal rejection, confirmation cancellation/acceptance through the actual routed handler, memory preservation, last-chat selection, Delete-key scope, restart persistence, real summary generation, corrected-unit/identifier preservation, token reduction, unchanged originals, checkpoint/usage persistence, recent-turn retention, invalid-anchor recovery, a real answer using summarized facts, repeated compaction, cancellation before inference, oversized-prefix rejection, reset and unloading.

Native UI verification additionally opened the right-click menu on an unselected synthetic chat, verified selection and the confirmation's correct title/default No, declined deletion, tested the Delete key, inspected the summary viewer and compaction confirmation, and checked menu/header readability. Confirmed deletion and summary reset were tested through isolated application/storage code, rather than deleting any real user conversation through desktop automation. The exact capture helper created for the UI check was stopped afterward.

Version 1.5 also reruns the existing 121 chat/vision/memory/thinking/embedding/personality checks, 26 avatar checks and one abrupt-parent-exit check: 170 checks in total. Evidence is in **verification/v1.5-*/** and **UI-CHECKS-1.5.md**. No test model or avatar is left running.
