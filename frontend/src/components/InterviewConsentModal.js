import './InterviewConsentModal.css';

function InterviewConsentModal({ open, onConfirm, onCancel }) {
  if (!open) {
    return null;
  }

  return (
    <div className="ConsentModal-overlay" role="dialog" aria-modal="true">
      <div className="ConsentModal">
        <div className="ConsentModal-header">
          <h2 className="ConsentModal-title">Proceed With Interview?</h2>
          <p className="ConsentModal-subtitle">
            The candidate passed screening. Do you want to start the interview now?
          </p>
        </div>
        <div className="ConsentModal-actions">
          <button className="ConsentModal-button" type="button" onClick={onCancel}>
            Not now
          </button>
          <button className="ConsentModal-button ConsentModal-button--primary" type="button" onClick={onConfirm}>
            Proceed
          </button>
        </div>
      </div>
    </div>
  );
}

export default InterviewConsentModal;
