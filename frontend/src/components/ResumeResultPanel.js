import './ResumeResultPanel.css';

function ResumeResultPanel({ resumeJson }) {
  const cleanedJson = (resumeJson || '').replace(/```json\s*|```/gi, '').trim();
  let parsed = null;

  try {
    parsed = JSON.parse(cleanedJson);
  } catch {
    parsed = null;
  }

  const name = parsed?.Name || 'Candidate';
  const experience = parsed?.['Experience in years'] || 'Not specified';
  const education = parsed?.Education || [];
  const languages = parsed?.Languages || [];
  const technicalSkills = parsed?.['Technical Skills'] || [];

  const normalizeToStrings = (value) => {
    if (!value) {
      return [];
    }

    if (Array.isArray(value)) {
      return value
        .map((item) => {
          if (typeof item === 'string') {
            return item;
          }
          if (typeof item === 'object' && item !== null) {
            return Object.values(item).filter(Boolean).join(' | ');
          }
          return String(item);
        })
        .filter(Boolean);
    }

    if (typeof value === 'object') {
      return [Object.values(value).filter(Boolean).join(' | ')].filter(Boolean);
    }

    return [String(value)];
  };

  const formatEducation = (value) => {
    if (Array.isArray(value)) {
      return value
        .map((item) => {
          if (typeof item === 'string') {
            return item;
          }
          if (typeof item === 'object' && item !== null) {
            const degree = item.Degree || item.degree;
            const institution = item.Institution || item.institution;
            const dates = item.Dates || item.dates;
            const location = item.Location || item.location;
            return [degree, institution, dates, location].filter(Boolean).join(' | ');
          }
          return String(item);
        })
        .filter(Boolean);
    }

    if (typeof value === 'object' && value !== null) {
      const degree = value.Degree || value.degree;
      const institution = value.Institution || value.institution;
      const dates = value.Dates || value.dates;
      const location = value.Location || value.location;
      return [[degree, institution, dates, location].filter(Boolean).join(' | ')].filter(Boolean);
    }

    return value ? [String(value)] : [];
  };

  const skills = normalizeToStrings(technicalSkills)
    .flatMap((entry) => entry.split(/[|,]/))
    .map((entry) => entry.trim())
    .filter(Boolean);
  const languageList = normalizeToStrings(languages);
  const educationList = formatEducation(education);

  return (
    <section className="ResumePanel">
      <div className="ResumePanel-header">
        <span className="ResumePanel-eyebrow">Extracted Resume</span>
        <h3 className="ResumePanel-title">{name}</h3>
      </div>
      <div className="ResumePanel-list">
        <div className="ResumePanel-item">
          <span className="ResumePanel-label">Experience</span>
          <span className="ResumePanel-value">{experience}</span>
        </div>
        <div className="ResumePanel-item">
          <span className="ResumePanel-label">Education</span>
          <span className="ResumePanel-value">
            {educationList.length > 0 ? educationList.join('; ') : 'Not specified'}
          </span>
        </div>
        <div className="ResumePanel-item">
          <span className="ResumePanel-label">Languages</span>
          <span className="ResumePanel-value">
            {languageList.length > 0 ? languageList.join(', ') : 'Not specified'}
          </span>
        </div>
      </div>
      <div className="ResumePanel-section">
        <span className="ResumePanel-label">Technical Skills</span>
        <div className="ResumePanel-tags">
          {skills.length > 0 ? (
            skills.map((skill) => (
              <span className="ResumePanel-tag" key={skill}>{skill}</span>
            ))
          ) : (
            <span className="ResumePanel-empty">No technical skills found.</span>
          )}
        </div>
      </div>
    </section>
  );
}

export default ResumeResultPanel;
