import { useState } from 'react';
import './JobUploadModal.css';

function JobUploadModal({ open, onClose, onUpload }) {
  const [file, setFile] = useState(null);
  const [jobName, setJobName] = useState('');

  if (!open) {
    return null;
  }

  const handleChange = (event) => {
    const nextFile = event.target.files && event.target.files[0];
    setFile(nextFile || null);
  };

  const handleSubmit = async () => {
    if (onUpload) {
      await onUpload(file, jobName.trim());
    }
  };

  return (
    <div className="JobModal-overlay" role="dialog" aria-modal="true">
      <div className="JobModal">
        <div className="JobModal-header">
          <div>
            <label className="JobModal-field">
              <span className="JobModal-label">Job name</span>
              <input
                className="JobModal-textInput"
                type="text"
                placeholder="e.g. Senior Product Designer"
                value={jobName}
                onChange={(event) => setJobName(event.target.value)}
              />
            </label>
            <h2 className="JobModal-title">Upload Job Description</h2>
            <p className="JobModal-subtitle">Add a file to start a new job profile.</p>
          </div>
          <button className="JobModal-close" type="button" onClick={onClose}>
            Close
          </button>
        </div>

        <label className="JobModal-dropzone">
          <input
            className="JobModal-input"
            type="file"
            accept=".pdf,.doc,.docx,.txt"
            onChange={handleChange}
          />
          <div className="JobModal-dropzoneText">
            {file ? file.name : 'Choose a file or drop it here'}
          </div>
          <span className="JobModal-hint">PDF, DOCX, or TXT</span>
        </label>

        <div className="JobModal-actions">
          <button
            className="JobModal-upload"
            type="button"
            onClick={handleSubmit}
            disabled={!file}
          >
            Upload
          </button>
        </div>
      </div>
    </div>
  );
}

export default JobUploadModal;
