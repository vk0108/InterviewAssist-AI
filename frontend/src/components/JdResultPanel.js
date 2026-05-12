import './JdResultPanel.css';

function JdResultPanel({ jdJson, jobName }) {
  let parsed = null;
  const cleanedJson = (jdJson || '').replace(/```json\s*|```/gi, '').trim();

  try {
    parsed = JSON.parse(cleanedJson);
  } catch {
    parsed = null;
  }

  const roleName = parsed?.role_name || jobName || 'Job description';
  const employmentType = parsed?.employment_type || 'Not specified';
  const experience = parsed?.experience || 'Not specified';
  const relevantExperience = parsed?.relevant_experience || 'Not specified';
  const primarySkills = Array.isArray(parsed?.primary_skills) ? parsed.primary_skills : [];
  const secondarySkills = Array.isArray(parsed?.secondary_skills) ? parsed.secondary_skills : [];

  return (
    <section className="JdPanel">
      <div className="JdPanel-header">
        <span className="JdPanel-eyebrow">Extracted JD</span>
        <h3 className="JdPanel-title">{roleName}</h3>
      </div>
      <div className="JdPanel-grid">
        <div className="JdPanel-card">
          <span className="JdPanel-label">Employment Type</span>
          <span className="JdPanel-value">{employmentType}</span>
        </div>
        <div className="JdPanel-card">
          <span className="JdPanel-label">Experience</span>
          <span className="JdPanel-value">{experience}</span>
        </div>
        <div className="JdPanel-card">
          <span className="JdPanel-label">Relevant Experience</span>
          <span className="JdPanel-value">{relevantExperience}</span>
        </div>
      </div>
      <div className="JdPanel-section">
        <span className="JdPanel-label">Primary Skills</span>
        <div className="JdPanel-tags">
          {primarySkills.length > 0 ? (
            primarySkills.map((skill) => (
              <span className="JdPanel-tag" key={skill}>{skill}</span>
            ))
          ) : (
            <span className="JdPanel-empty">No primary skills found.</span>
          )}
        </div>
      </div>
      <div className="JdPanel-section">
        <span className="JdPanel-label">Secondary Skills</span>
        <div className="JdPanel-tags">
          {secondarySkills.length > 0 ? (
            secondarySkills.map((skill) => (
              <span className="JdPanel-tag JdPanel-tag--muted" key={skill}>{skill}</span>
            ))
          ) : (
            <span className="JdPanel-empty">No secondary skills found.</span>
          )}
        </div>
      </div>
    </section>
  );
}

export default JdResultPanel;
