import ResultView from "./result-view";

export default async function ResultPage({ params }: { params: Promise<{ invoiceNo: string }> }) {
  const { invoiceNo } = await params;
  return <ResultView invoiceNo={invoiceNo} />;
}
