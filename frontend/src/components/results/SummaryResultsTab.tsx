import { useEffect, useState } from "react";
import type { FormResults } from "../../types/results";
import { getFormResults } from "../../api/results";
import { QuestionResultCard } from "./QuestionResultCard";
import { ScoreDistributionChart } from "./ScoreDistributionChart";

type ResultsState = "loading" | "ready" | "error";

interface SummaryResultsTabProps {
  formId: string;
  reloadKey: number;
}

export function SummaryResultsTab({ formId, reloadKey }: Readonly<SummaryResultsTabProps>) {
  const [state, setState] = useState<ResultsState>("loading");
  const [results, setResults] = useState<FormResults | null>(null);

  // Reset to loading when the form or a refetch is requested. Adjusting state during render
  // (rather than in the effect) avoids an extra commit-then-effect render pass.
  const [prevQuery, setPrevQuery] = useState([formId, reloadKey]);
  if (prevQuery[0] !== formId || prevQuery[1] !== reloadKey) {
    setPrevQuery([formId, reloadKey]);
    setState("loading");
  }

  useEffect(() => {
    let cancelled = false;

    getFormResults(formId)
      .then((data) => {
        if (cancelled) return;

        setResults(data);
        setState("ready");
      })
      .catch(() => {
        if (cancelled) return;
        setState("error");
      });

    return () => {
      cancelled = true;
    };
  }, [formId, reloadKey]);

  if (state === "loading")
    return <p className="py-16 text-center text-gray-500">Loading results…</p>;

  if (state === "error" || !results)
    return <p className="py-16 text-center text-red-600">Could not load the results.</p>;

  if (results.submissionCount === 0) {
    return (
      <div className="py-16 text-center text-gray-400">
        <p className="text-lg">No submissions yet.</p>
        <p className="mt-1 text-sm">Share the link and answers will show up here.</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-gray-500">
        {results.submissionCount} submission{results.submissionCount !== 1 ? "s" : ""}
      </p>

      {results.isGraded && results.scoreDistribution.length > 0 && (
        <ScoreDistributionChart
          buckets={results.scoreDistribution}
          totalPoints={results.totalPoints}
        />
      )}

      {results.questions.map((question) => (
        <QuestionResultCard key={question.questionId} question={question} />
      ))}
    </div>
  );
}
