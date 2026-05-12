import { useState } from 'react';
import './ResumeUploadModal.css';

function ResumeUploadModal({ open, onClose, onUpload }) {
  const [file, setFile] = useState(null);

  if (!open) {
    return null;
  }

  const handleChange = (event) => {
    const nextFile = event.target.files && event.target.files[0];
    setFile(nextFile || null);
  };

  const handleSubmit = async () => {
    if (onUpload) {
      await onUpload(file);
    }
  };

  return (
    <div className="ResumeModal-overlay" role="dialog" aria-modal="true">
      <div className="ResumeModal">
        <div className="ResumeModal-header">
          <div>
            <h2 className="ResumeModal-title">Upload Resume</h2>
            <p className="ResumeModal-subtitle">Add the candidate resume to proceed.</p>
          </div>
          <button className="ResumeModal-close" type="button" onClick={onClose}>
            Close
          </button>
        </div>

        <label className="ResumeModal-dropzone">
          <input
            className="ResumeModal-input"
            type="file"
            accept=".pdf,.doc,.docx,.txt"
            onChange={handleChange}
          />
          <div className="ResumeModal-dropzoneText">
            {file ? file.name : 'Choose a file or drop it here'}
          </div>
          <span className="ResumeModal-hint">PDF, DOCX, or TXT</span>
        </label>

        <div className="ResumeModal-actions">
          <button
            className="ResumeModal-upload"
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

export default ResumeUploadModal;
