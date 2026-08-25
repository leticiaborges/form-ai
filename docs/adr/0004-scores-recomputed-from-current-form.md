# Scores are recomputed from the current form, and matched on option text

A score could have been frozen at the moment of submission — the reading most form tools take, and the one that makes a stored score a permanent record of a moment. We took the other reading: a score is *derived*, so it always reflects the form as it now stands. When an editor save changes anything that decides a score, every submission of that form is rescored in the same transaction.

The alternative loses badly on the case that actually happens. An owner publishes a graded form, thirty people answer, and the owner then notices the answer key marks the wrong option. Under frozen scores those thirty scores stay wrong for ever and nothing in the product can fix them. Under recomputation, correcting the key corrects the scores.

This forces a second decision. A stored submission does not know which option row a respondent picked — only the text of it, because [ADR 0001](./0001-selected-option-text-snapshot.md) deliberately removed that reference. So rescoring can only match on text. Rather than have submitting match on ids and rescoring match on text — two code paths that would slowly disagree — **both match on text**: `SubmitFormHandler` resolves the submitted option ids to their text and then calls the same `SubmissionScorer` the rescore path calls.

Whether a save needs a rescore is decided by a *grading fingerprint* (`GradingFingerprint`), taken before and after the save over the questions that already existed. Comparing one string is harder to get subtly wrong than threading a "did this affect grading?" flag through every branch of the editor diff.

## Consequences

- **Renaming an option changes the scores of submissions already made, silently.** A respondent who picked "Paris" is recorded as having answered "Paris"; if the owner later renames that option to "Paris, France", the recorded text no longer matches the correct option and the respondent is rescored as wrong. Nothing warns the owner. This is the sharpest edge of the decision and it is accepted: the recorded text is what the respondent actually saw and chose, and a rename is a deliberate act by the owner.
- Option text being unique within a question is now load-bearing for correctness, not just for grouping. It is still enforced only in the application layer (`QuestionOptionValidator`), with no database constraint.
- Adding a question never triggers a rescore. An unanswered question scores 0 and no denominator is stored, so an addition cannot move any stored total. Deleting one does trigger a rescore.
- Scoring lives in `FormAI.Domain/Scoring/` as pure functions over entities, so "the owner edits a question's points" reuses `RescoreFormSubmissionsHandler` rather than reimplementing the rules. That flow now exists: points are an ordinary field in the form editor, so changing a question from 1 point to 5 and saving silently rewrites every stored score for that form. The fingerprint already covered `Points`, so this needed no new machinery — but it moved the silent-rescore behaviour from hypothetical to something an owner reaches by typing in a box.
- Rescoring loads every submission of the form with its answers on each grading-relevant save. Fine at the sizes this handles today; it is the first thing to revisit if a form ever collects thousands of submissions.
