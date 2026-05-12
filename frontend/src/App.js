import { useEffect, useState, useRef, useCallback } from 'react';
import * as SpeechSDK from 'microsoft-cognitiveservices-speech-sdk';
import JobUploadModal from './components/JobUploadModal';
import JdResultPanel from './components/JdResultPanel';
import ResumeResultPanel from './components/ResumeResultPanel';
import ResumeUploadModal from './components/ResumeUploadModal';
import InterviewReport from './components/InterviewReport';
import './App.css';

function App() {
  const [isUploadOpen, setIsUploadOpen] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [uploadError, setUploadError] = useState('');
  const [jdResult, setJdResult] = useState(null);
  const [resumeResult, setResumeResult] = useState(null);
  const [screeningResult, setScreeningResult] = useState(null);
  const [strongOnly, setStrongOnly] = useState(false);
  const [jobHistory, setJobHistory] = useState(() => {
    try {
      const stored = localStorage.getItem('jobHistory');
      return stored ? JSON.parse(stored) : [];
    } catch {
      return [];
    }
  });
  const [activeJobId, setActiveJobId] = useState(null);
  const [actionStatus, setActionStatus] = useState('');
  const [actionError, setActionError] = useState('');
  const [isResumeOpen, setIsResumeOpen] = useState(false);
  const [pendingMode, setPendingMode] = useState('');
  const [actionMode, setActionMode] = useState('');
  const [firstQuestion, setFirstQuestion] = useState('');
  const [sessionId, setSessionId] = useState('');
  const [currentQuestion, setCurrentQuestion] = useState('');
  const [answerInput, setAnswerInput] = useState('');
  const [awaitingFollowUps, setAwaitingFollowUps] = useState(false);
  const [pendingFollowUps, setPendingFollowUps] = useState([]);
  const [evaluationText, setEvaluationText] = useState('');
  const [managerAssessment, setManagerAssessment] = useState('');
  const [interviewCompleted, setInterviewCompleted] = useState(false);
  const [showReport, setShowReport] = useState(false);
  const [averageScore, setAverageScore] = useState(null);
  const [totalQuestions, setTotalQuestions] = useState(0);
  const [isListening, setIsListening] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [displayedQuestion, setDisplayedQuestion] = useState('');
  const recognitionRef = useRef(null);
  const pronunciationScoreRef = useRef(null);
  const pronScoresRef = useRef([]);
  const streamIntervalRef = useRef(null);

  useEffect(() => {
    // Clear any previously persisted transient state so the app starts fresh on reload
    localStorage.removeItem('jdResult');
    localStorage.removeItem('resumeResult');
    localStorage.removeItem('screeningResult');
    localStorage.removeItem('strongOnly');
    localStorage.removeItem('actionMode');
  }, []);

  useEffect(() => {
    localStorage.setItem('jobHistory', JSON.stringify(jobHistory));
  }, [jobHistory]);

  // Azure Speech SDK continuous recognition with 10s silence cutoff
  const startListening = useCallback(async () => {
    setIsListening(true);
    setAnswerInput('');
    pronScoresRef.current = [];
    pronunciationScoreRef.current = null;

    try {
      const res = await fetch('http://localhost:5000/api/speech/token');
      const { token, region } = await res.json();

      const speechConfig = SpeechSDK.SpeechConfig.fromAuthorizationToken(token, region);
      speechConfig.speechRecognitionLanguage = 'en-US';

      const audioConfig = SpeechSDK.AudioConfig.fromDefaultMicrophoneInput();
      const recognizer = new SpeechSDK.SpeechRecognizer(speechConfig, audioConfig);
      recognitionRef.current = recognizer;

      // Pronunciation assessment
      const pronConfig = new SpeechSDK.PronunciationAssessmentConfig(
        '',
        SpeechSDK.PronunciationAssessmentGradingSystem.HundredMark,
        SpeechSDK.PronunciationAssessmentGranularity.Phoneme,
        false
      );
      pronConfig.enableProsodyAssessment = true;
      pronConfig.applyTo(recognizer);

      let fullTranscript = '';
      let silenceTimer = null;

      const resetSilenceTimer = () => {
        if (silenceTimer) clearTimeout(silenceTimer);
        silenceTimer = setTimeout(() => {
          recognizer.stopContinuousRecognitionAsync();
        }, 10000);
      };

      recognizer.recognizing = (s, e) => {
        if (e.result.text) {
          setAnswerInput(fullTranscript + e.result.text);
          resetSilenceTimer();
        }
      };

      recognizer.recognized = (s, e) => {
        if (e.result.reason === SpeechSDK.ResultReason.RecognizedSpeech && e.result.text) {
          fullTranscript += e.result.text + ' ';
          setAnswerInput(fullTranscript.trim());
          resetSilenceTimer();

          const pronResult = SpeechSDK.PronunciationAssessmentResult.fromResult(e.result);
          if (pronResult && pronResult.pronunciationScore > 0) {
            pronScoresRef.current.push(pronResult.pronunciationScore);
            const arr = pronScoresRef.current;
            pronunciationScoreRef.current = arr.reduce((a, b) => a + b, 0) / arr.length;
            console.log('[STT] pronunciationScore segment:', pronResult.pronunciationScore, 'running avg:', pronunciationScoreRef.current);
          }
        }
      };

      recognizer.canceled = (s, e) => {
        if (silenceTimer) clearTimeout(silenceTimer);
        recognizer.stopContinuousRecognitionAsync();
      };

      recognizer.sessionStopped = (s, e) => {
        if (silenceTimer) clearTimeout(silenceTimer);
        recognizer.stopContinuousRecognitionAsync(() => {
          recognizer.close();
          recognitionRef.current = null;
          setIsListening(false);
        });
      };

      recognizer.startContinuousRecognitionAsync(
        () => resetSilenceTimer(),
        (err) => { console.error('[STT] Start error:', err); setIsListening(false); }
      );
    } catch (err) {
      console.error('[STT] Error:', err);
      setIsListening(false);
    }
  }, []);

  const stopListening = useCallback(() => {
    const recognizer = recognitionRef.current;
    if (!recognizer) { setIsListening(false); return; }
    recognizer.stopContinuousRecognitionAsync(() => {
      recognizer.close();
      recognitionRef.current = null;
      setIsListening(false);
    });
  }, []);

  const speakAndStream = (text, audioBase64) => {
    if (streamIntervalRef.current) {
      clearInterval(streamIntervalRef.current);
      streamIntervalRef.current = null;
    }
    if (!text) {
      setDisplayedQuestion('');
      return;
    }
    setDisplayedQuestion('');
    let idx = 0;
    const speed = text.length > 200 ? 20 : 35;
    streamIntervalRef.current = setInterval(() => {
      idx = Math.min(idx + 1, text.length);
      setDisplayedQuestion(text.slice(0, idx));
      if (idx >= text.length) {
        clearInterval(streamIntervalRef.current);
        streamIntervalRef.current = null;
      }
    }, speed);

    if (audioBase64) {
      try {
        const audio = new Audio(`data:audio/mp3;base64,${audioBase64}`);
        audio.play().catch(() => {});
      } catch {}
    }
  };

  const openUpload = () => setIsUploadOpen(true);
  const closeUpload = () => setIsUploadOpen(false);

  const loadJob = (job) => {
    resetJob();
    setActiveJobId(job.id);
    setJdResult({ jobName: job.jobName, jdJson: job.jdJson });
  };

  const deleteJob = (e, jobId) => {
    e.stopPropagation();
    setJobHistory((prev) => prev.filter((j) => j.id !== jobId));
    if (activeJobId === jobId) {
      resetJob();
    }
  };

  const handleUpload = async (file, jobName) => {
    if (!file) {
      return;
    }

    try {
      setUploadError('');
      setIsUploading(true);
      setIsUploadOpen(false);
      const formData = new FormData();
      formData.append('file', file);
      formData.append('jobName', jobName || '');

      const response = await fetch('http://localhost:5000/api/jd/upload', {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        throw new Error('Upload failed.');
      }

      const data = await response.json();
      const jdJson = data?.jdJson || '';
      const newJob = {
        id: Date.now().toString(),
        jobName: jobName || '',
        jdJson,
        createdAt: new Date().toISOString(),
      };
      setJobHistory((prev) => [newJob, ...prev]);
      setActiveJobId(newJob.id);
      setJdResult({
        jobName: jobName || '',
        jdJson,
      });
    } catch (error) {
      console.error(error);
      setUploadError('Upload failed. Please try again.');
    } finally {
      setIsUploading(false);
    }
  };

  const criteriaValue = strongOnly ? 'strong match' : 'partial match';

  const runWorkflow = async (mode, resumeJson) => {
    if (!jdResult?.jdJson || !resumeJson) {
      return;
    }

    try {
      setActionError('');
      setActionStatus('Starting workflow...');

      const endpoint =
        mode === 'interview'
          ? 'http://localhost:5000/api/interview/start'
          : `http://localhost:5000/api/${mode}/run`;

      const payload =
        mode === 'interview'
          ? {
              criteria: criteriaValue,
              jobDescriptionJson: jdResult.jdJson,
              resumeJson,
            }
          : {
              criteria: criteriaValue,
              jobDescriptionJson: jdResult.jdJson,
              resumeJson,
            };

      const response = await fetch(endpoint, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        throw new Error('Workflow request failed.');
      }

      const data = await response.json();
      setActionStatus(data?.message || 'Workflow started.');
      if (mode === 'interview' && data?.sessionId && data?.firstQuestion) {
        setSessionId(data.sessionId);
        setFirstQuestion(data.firstQuestion);
        setCurrentQuestion(data.firstQuestion);
        speakAndStream(data.firstQuestion, data.audioBase64);
        setAwaitingFollowUps(false);
        setPendingFollowUps([]);
        setEvaluationText('');
        setManagerAssessment('');
        setInterviewCompleted(false);
        setShowReport(false);
        setAverageScore(null);
        setTotalQuestions(0);
      }
    } catch (error) {
      console.error(error);
      setActionError('Workflow request failed.');
      setActionStatus('');
    }
  };

  const runScreening = async (resumeJson) => {
    if (!jdResult?.jdJson || !resumeJson) {
      return null;
    }

    try {
      setActionError('');
      setActionStatus('Running screening...');

      const response = await fetch('http://localhost:5000/api/screening/run', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          criteria: criteriaValue,
          jobDescriptionJson: jdResult.jdJson,
          resumeJson,
        }),
      });

      if (!response.ok) {
        throw new Error('Screening request failed.');
      }

      const data = await response.json();
      const result = {
        screeningText: data?.screeningText || '',
        verdictLine: data?.verdictLine || '',
        passesCriteria: Boolean(data?.passesCriteria),
      };
      setScreeningResult(result);
      setActionStatus('Screening completed.');
      return result;
    } catch (error) {
      console.error(error);
      setActionError('Screening request failed.');
      setActionStatus('');
      return null;
    }
  };

  const openResumeModal = (mode) => {
    setPendingMode(mode);
    setActionMode(mode);
    setIsResumeOpen(true);
  };

  const closeResumeModal = () => {
    setIsResumeOpen(false);
  };

  const handleResumeUpload = async (file) => {
    if (!file) {
      return;
    }

    try {
      setActionError('');
      setActionStatus('Uploading resume...');
      setIsResumeOpen(false);
      const formData = new FormData();
      formData.append('file', file);

      const response = await fetch('http://localhost:5000/api/resume/upload', {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        throw new Error('Resume upload failed.');
      }

      const data = await response.json();
      const extracted = data?.resumeJson || '';
      setResumeResult({
        resumeJson: extracted,
      });

      if (pendingMode) {
        setActionMode(pendingMode);
        const screening = await runScreening(extracted);
        if (pendingMode === 'interview' && !screening?.passesCriteria) {
          setActionStatus('Candidate did not pass screening.');
        }
        setPendingMode('');
      }
    } catch (error) {
      console.error(error);
      setActionError('Resume upload failed.');
      setActionStatus('');
    }
  };

  const resetJob = () => {
    if (streamIntervalRef.current) {
      clearInterval(streamIntervalRef.current);
      streamIntervalRef.current = null;
    }
    setDisplayedQuestion('');
    setJdResult(null);
    setResumeResult(null);
    setScreeningResult(null);
    setStrongOnly(false);
    setActionStatus('');
    setActionError('');
    setFirstQuestion('');
    setSessionId('');
    setCurrentQuestion('');
    setAnswerInput('');
    setAwaitingFollowUps(false);
    setPendingFollowUps([]);
    setEvaluationText('');
    setManagerAssessment('');
    setInterviewCompleted(false);
    setAverageScore(null);
    setTotalQuestions(0);
    setUploadError('');
    setIsUploadOpen(false);
    setIsResumeOpen(false);
    setPendingMode('');
    setActionMode('');
    setActiveJobId(null);
  };

  const submitInterviewAnswer = async () => {
    if (!sessionId) {
      setActionError('Start the interview first.');
      return;
    }

    try {
      setActionError('');
      setActionStatus('Submitting answer...');
      setIsSubmitting(true);

      const scoreToSend = pronunciationScoreRef.current;
      console.log('[STT] Submitting pronunciationScore:', scoreToSend);

      const response = await fetch('http://localhost:5000/api/interview/answer', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          sessionId,
          answer: answerInput,
          pronunciationScore: scoreToSend,
        }),
      });

      if (!response.ok) {
        throw new Error('Answer submission failed.');
      }

      pronunciationScoreRef.current = null;
      pronScoresRef.current = [];
      const data = await response.json();
      setEvaluationText(data?.evaluation || '');
      setManagerAssessment(data?.managerAssessment || '');
      setActionStatus(data?.status || '');
      setAverageScore(data?.averageScore ?? null);
      setTotalQuestions(data?.totalQuestions ?? 0);

      setAwaitingFollowUps(Boolean(data?.awaitingFollowUps));
      setPendingFollowUps(data?.followUpQuestions || []);

      if (data?.nextQuestion) {
        setCurrentQuestion(data.nextQuestion);
        speakAndStream(data.nextQuestion, data.audioBase64);
        setAnswerInput('');
        if (!data?.completed) {
          setEvaluationText('');
          setManagerAssessment('');
        }
      }

      if (data?.completed) {
        setInterviewCompleted(true);
        setShowReport(true);
      }
    } catch (error) {
      console.error(error);
      setActionError('Answer submission failed.');
    } finally {
      setIsSubmitting(false);
    }
  };

  if (showReport && sessionId) {
    return (
      <div className="App">
        <InterviewReport
          sessionId={sessionId}
          onBack={() => {
            setShowReport(false);
            setSessionId('');
            setInterviewCompleted(false);
            setCurrentQuestion('');
            setAnswerInput('');
            setEvaluationText('');
            setManagerAssessment('');
            setAverageScore(null);
            setTotalQuestions(0);
          }}
        />
      </div>
    );
  }

  return (
    <div className="App">
      <div className={`App-shell${sessionId ? ' App-shell--interview' : ''}`}>
        {!sessionId && (
        <aside className="History">
          <div className="History-header">
            <span className="History-title">Job History</span>
            <button className="History-filter" type="button" onClick={openUpload}>
              + New
            </button>
          </div>
          <ul className="History-list">
            {jobHistory.length === 0 && (
              <li className="History-empty">No jobs yet. Create one to get started.</li>
            )}
            {jobHistory.map((job) => (
              <li
                className={`History-item${activeJobId === job.id ? ' History-item--active' : ''}`}
                key={job.id}
                onClick={() => loadJob(job)}
              >
                <div className="History-itemHeader">
                  <div className="History-role">{job.jobName || 'Untitled Job'}</div>
                  <button
                    className="History-delete"
                    type="button"
                    title="Delete job"
                    onClick={(e) => deleteJob(e, job.id)}
                  >
                    &times;
                  </button>
                </div>
                <div className="History-meta">
                  Created {new Date(job.createdAt).toLocaleDateString('en-US', { day: '2-digit', month: 'short', year: 'numeric' })}
                </div>
              </li>
            ))}
          </ul>
        </aside>
        )}

        <main className="Stage">
          {sessionId ? (
            <div className="Stage-body">
              <div className="Stage-screening">
                <span className="Stage-sectionLabel">
                  {awaitingFollowUps ? 'Follow-up Question' : 'Current Question'}
                </span>
                <p className="Stage-screeningLine">{displayedQuestion || currentQuestion || firstQuestion}</p>
                <div className="Stage-proceedActions">
                  <button
                    className="Stage-option"
                    type="button"
                    onClick={isListening ? stopListening : startListening}
                  >
                    {isListening ? 'Stop Mic' : 'Record Answer'}
                  </button>
                </div>
                <label className="Stage-sectionLabel" htmlFor="answer-input">
                  Your answer
                </label>
                <textarea
                  id="answer-input"
                  className="Stage-screeningLine"
                  rows={4}
                  value={answerInput}
                  onChange={(e) => setAnswerInput(e.target.value)}
                  placeholder="Speak or type your response..."
                />
                <div className="Stage-proceedActions">
                    <button
                    className="Stage-option Stage-option--primary"
                    type="button"
                    onClick={submitInterviewAnswer}
                    disabled={isSubmitting || !answerInput.trim()}
                  >
                    {isSubmitting ? 'Submitting...' : 'Submit'}
                  </button>
                  <button className="Stage-option Stage-option--ghost" type="button" onClick={resetJob}>
                    Reset
                  </button>
                </div>
              </div>

              {actionStatus && <p className="Stage-status">{actionStatus}</p>}
              {actionError && <p className="Stage-error">{actionError}</p>}
              {evaluationText && (
                <div className="Stage-screening">
                  <span className="Stage-sectionLabel">Evaluation</span>
                  <p className="Stage-screeningLine">{evaluationText}</p>
                </div>
              )}
              {managerAssessment && (
                <div className="Stage-screening">
                  <span className="Stage-sectionLabel">Manager Assessment</span>
                  <p className="Stage-screeningLine">{managerAssessment}</p>
                </div>
              )}
            </div>
          ) : jdResult ? (
            <div className="Stage-body">
              {!screeningResult && (
              <div className="Stage-toggle">
                <span className="Stage-toggleLabel">Strong matches only</span>
                <label className="Toggle">
                  <input
                    className="Toggle-input"
                    type="checkbox"
                    checked={strongOnly}
                    onChange={(event) => setStrongOnly(event.target.checked)}
                  />
                  <span className="Toggle-track">
                    <span className="Toggle-thumb" />
                  </span>
                </label>
              </div>
              )}
              {resumeResult?.resumeJson ? (
                <ResumeResultPanel resumeJson={resumeResult.resumeJson} />
              ) : (
                <JdResultPanel jdJson={jdResult.jdJson} jobName={jdResult.jobName} />
              )}
              {screeningResult && (
                <div className="Stage-screening">
                  <span className="Stage-sectionLabel">Screening Result</span>
                  <div className="Stage-screeningText">
                    {screeningResult.screeningText
                      ?.replace(/^\s*[-•]\s*/gm, '')
                      ?.split('\n')
                      ?.filter((line) => line.trim().length > 0)
                      ?.map((line) => (
                        <p className="Stage-screeningLine" key={line}>{line}</p>
                      ))}
                  </div>
                  <div className="Stage-screeningRow">
                    <span className={
                      screeningResult.passesCriteria ? 'Stage-screeningPass' : 'Stage-screeningFail'
                    }>
                      {screeningResult.passesCriteria ? 'Passed criteria' : 'Did not pass'}
                    </span>
                  </div>
                </div>
              )}
              <div className="Stage-actions">
                {!resumeResult?.resumeJson ? (
                  <>
                    <button className="Stage-option" type="button" onClick={() => openResumeModal('screening')}>
                      Only screening
                    </button>
                    <button
                      className="Stage-option Stage-option--primary"
                      type="button"
                      onClick={() => openResumeModal('interview')}
                    >
                      Complete Interview
                    </button>
                  </>
                ) : (
                  screeningResult?.passesCriteria && (actionMode === 'interview' || actionMode === '') && (
                    <div className="Stage-proceed">
                      <span className="Stage-proceedLabel">Proceed with interview?</span>
                      <div className="Stage-proceedActions">
                        <button
                          className="Stage-option"
                          type="button"
                          onClick={() => setActionStatus('Interview cancelled.')}
                        >
                          No
                        </button>
                        <button
                          className="Stage-option Stage-option--primary"
                          type="button"
                          onClick={() => runWorkflow('interview', resumeResult.resumeJson)}
                        >
                          Yes
                        </button>
                      </div>
                    </div>
                  )
                  )}
                <button className="Stage-option Stage-option--ghost" type="button" onClick={resetJob}>
                  Reset
                </button>
              </div>
              {actionStatus && <p className="Stage-status">{actionStatus}</p>}
              {actionError && <p className="Stage-error">{actionError}</p>}
              {evaluationText && (
                <div className="Stage-screening">
                  <span className="Stage-sectionLabel">Evaluation</span>
                  <p className="Stage-screeningLine">{evaluationText}</p>
                </div>
              )}
              {managerAssessment && (
                <div className="Stage-screening">
                  <span className="Stage-sectionLabel">Manager Assessment</span>
                  <p className="Stage-screeningLine">{managerAssessment}</p>
                </div>
              )}
            </div>
          ) : (
            <div className="Stage-center">
              <button className="Create-button" type="button" onClick={openUpload}>
                <span className="Create-plus">+</span>
                <span className="Create-text">Create Job</span>
              </button>
              <p className="Stage-note">
                Start a new role profile and keep candidates organized.
              </p>
              {isUploading && <p className="Stage-status">Uploading and extracting...</p>}
              {uploadError && <p className="Stage-error">{uploadError}</p>}
            </div>
          )}
        </main>
      </div>
      <JobUploadModal open={isUploadOpen} onClose={closeUpload} onUpload={handleUpload} />
      <ResumeUploadModal open={isResumeOpen} onClose={closeResumeModal} onUpload={handleResumeUpload} />
    </div>
  );
}

export default App;
