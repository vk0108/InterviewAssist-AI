import { useEffect, useState } from 'react';
import './InterviewReport.css';

function ratingColor(rating) {
  if (rating >= 4.5) return '#2e7d32';
  if (rating >= 3.5) return '#66bb6a';
  if (rating >= 2.5) return '#ffa726';
  if (rating >= 1.5) return '#e65100';
  return '#c62828';
}

function scoreBarColor(score) {
  if (score >= 4.0) return '#2e7d32';
  if (score >= 3.0) return '#66bb6a';
  if (score >= 2.0) return '#ffa726';
  return '#c62828';
}

export default function InterviewReport({ sessionId, onBack }) {
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    fetch(`http://localhost:5000/api/interview/report/${sessionId}`)
      .then((res) => {
        if (!res.ok) throw new Error('Report not available');
        return res.json();
      })
      .then(setData)
      .catch((err) => setError(err.message))
      .finally(() => setLoading(false));
  }, [sessionId]);

  const downloadPdf = () => {
    window.open(`http://localhost:5000/api/interview/report/${sessionId}/pdf`, '_blank');
  };

  if (loading) return <div className="Report"><p>Generating report...</p></div>;
  if (error) return <div className="Report"><p style={{ color: '#c62828' }}>{error}</p></div>;
  if (!data) return null;

  const { candidateName, averageScore, totalQuestions, report } = data;
  const pct = ((averageScore / 5) * 100).toFixed(1);

  return (
    <div className="Report">
      <div className="Report-header">
        <div className="Report-headerLeft">
          <h1>Interview Report</h1>
          <p>{candidateName}</p>
        </div>
        <div className="Report-headerRight">
          <div>Date: {new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}</div>
          <button className="Report-download" onClick={downloadPdf}>Download PDF</button>
        </div>
      </div>

      {report?.quickRecap && (
        <div className="Report-section">
          <div className="Report-sectionTitle">Quick Recap</div>
          <p className="Report-recap">{report.quickRecap}</p>
        </div>
      )}

      <div className="Report-section">
        <div className="Report-scoreBox">
          <div className="Report-scoreBig">{averageScore?.toFixed(2)} / 5.0</div>
          <div className="Report-scoreMeta">Questions: {totalQuestions}</div>
          <div className="Report-scoreBar">
            <div
              className="Report-scoreBarFill"
              style={{ width: `${pct}%`, background: scoreBarColor(averageScore) }}
            />
          </div>
        </div>
      </div>

      {report?.technicalTraining?.length > 0 && (
        <div className="Report-section">
          <div className="Report-sectionTitle">Technical Training</div>
          <table className="Report-table">
            <thead>
              <tr><th>Skill</th><th>Rating</th><th>Summary</th></tr>
            </thead>
            <tbody>
              {report.technicalTraining.map((item, i) => (
                <tr key={i}>
                  <td style={{ fontWeight: 600 }}>{item.title}</td>
                  <td>
                    <span className="Report-ratingBadge" style={{ background: ratingColor(item.rating) }}>
                      {item.rating?.toFixed(1)}/5
                    </span>
                  </td>
                  <td>{item.summary}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {report?.softSkills?.length > 0 && (
        <div className="Report-section">
          <div className="Report-sectionTitle">Soft Skills</div>
          <table className="Report-table">
            <thead>
              <tr><th>Skill</th><th>Rating</th><th>Summary</th></tr>
            </thead>
            <tbody>
              {report.softSkills.map((item, i) => (
                <tr key={i}>
                  <td style={{ fontWeight: 600 }}>{item.title}</td>
                  <td>
                    <span className="Report-ratingBadge" style={{ background: ratingColor(item.rating) }}>
                      {item.rating?.toFixed(1)}/5
                    </span>
                  </td>
                  <td>{item.summary}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {report?.discussionTopics?.length > 0 && (
        <div className="Report-section">
          <div className="Report-sectionTitle">Summary of Discussion</div>
          {report.discussionTopics.map((topic, i) => (
            <div className="Report-topic" key={i}>
              <div className="Report-topicTitle">{topic.title}</div>
              <div className="Report-topicSummary">{topic.summary}</div>
            </div>
          ))}
        </div>
      )}

      {report?.interviewerComments && (
        <div className="Report-section">
          <div className="Report-sectionTitle">Interviewer Comments</div>
          <div className="Report-comments">{report.interviewerComments}</div>
        </div>
      )}

      <button className="Report-back" onClick={onBack}>Back to Home</button>
    </div>
  );
}
