# InterviewAssist — Agentic AI Interview Automation System

An end-to-end agentic AI platform that autonomously screens candidates, conducts voice-based interviews, and generates structured hiring reports — eliminating repetitive TA involvement through intelligent automation.

---

## Features

- **Job Description Parsing** — Upload a PDF/DOCX JD and extract structured role requirements using Azure Document Intelligence
- **Resume Screening** — Automatically score and verdict a candidate resume against the JD criteria
- **AI-Driven Interview** — Multi-turn, voice-based interview conducted by an AI interviewer with dynamic follow-up questions
- **Real-Time Speech** — Continuous speech-to-text with 10-second silence detection and pronunciation assessment via Azure Cognitive Services
- **Text-to-Speech** — Interview questions read aloud by Azure Neural TTS
- **Per-Answer Scoring** — Each answer evaluated and scored (1–5) in real time
- **Instant PDF Report** — Structured hiring report with technical skills, soft skills, discussion summary, and interviewer comments generated on interview completion
- **Persistent Job History** — Previous job profiles saved locally for re-use

---

## Architecture

```
InterviewAssist Agentic AI/
├── Agents/                  # ASP.NET Core backend (.NET 9)
│   ├── Program.cs           # API endpoints (screening, interview, report, speech token)
│   ├── InterviewWorkflow.cs # Orchestrates the full interview pipeline
│   ├── AgentFactory.cs      # Creates AI agent instances
│   ├── ConvoSumm.cs         # LLM-generated report payload
│   ├── ReportGeneration.cs  # QuestPDF report builder
│   ├── SpeechToText.cs      # Azure Speech SDK (STT + pronunciation assessment)
│   ├── TextToSpeech.cs      # Azure Neural TTS
│   ├── DocumentExtractionService.cs  # Azure Document Intelligence
│   └── EnvironmentConfig.cs
└── frontend/                # React 19 frontend
    └── src/
        ├── App.js           # Main app shell + interview flow
        └── components/
            ├── JobUploadModal       # JD file upload
            ├── ResumeUploadModal    # Resume file upload
            ├── JdResultPanel        # Extracted JD display
            ├── ResumeResultPanel    # Parsed resume display
            ├── InterviewReport      # Post-interview report page
            └── InterviewConsentModal
```

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | C#, ASP.NET Core (.NET 9), Minimal API |
| AI Orchestration | Microsoft.Agents.AI.Workflows, Microsoft.Agents.AI.OpenAI |
| LLM | Azure OpenAI (GPT-4o-mini) |
| Document Parsing | Azure Document Intelligence |
| Speech-to-Text | Azure Cognitive Services Speech SDK (continuous + pronunciation assessment) |
| Text-to-Speech | Azure Neural TTS |
| PDF Generation | QuestPDF |
| Frontend | React 19, Azure Cognitive Services Speech SDK (JS) |

---

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)
- [Node.js 18+](https://nodejs.org/)
- Azure subscription with the following services provisioned:
  - Azure OpenAI (GPT-4o-mini deployment)
  - Azure Document Intelligence
  - Azure Cognitive Services Speech (STT + TTS)

---

## Setup

### 1. Clone the repository

```bash
git clone https://github.com/your-username/InterviewAssist-Agentic-AI.git
cd "InterviewAssist-Agentic-AI"
```

### 2. Install and run the backend

```bash
cd Agents
dotnet restore
dotnet run
# Backend runs on http://localhost:5000
```

### 3. Install and run the frontend

```bash
cd frontend
npm install
npm start
# Frontend runs on http://localhost:3000
```

---

## Usage

1. **Create a Job** — Click **+ New** and upload a JD (PDF or DOCX). The system extracts structured requirements.
2. **Upload a Resume** — Click **Complete Interview** or **Only Screening** and upload the candidate's resume.
3. **Screening** — The AI automatically screens the resume against the JD and returns a pass/fail verdict.
4. **Interview** — If the candidate passes screening, click **Yes** to start the voice interview. The AI asks questions, listens to spoken responses, and asks follow-ups.
5. **Report** — After the interview completes, a full hiring report is shown in-browser and can be downloaded as a PDF.

---

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/jd/upload` | Upload and extract a job description |
| POST | `/api/resume/upload` | Upload and extract a resume |
| POST | `/api/screening/run` | Run candidate screening |
| POST | `/api/interview/start` | Start an interactive interview session |
| POST | `/api/interview/answer` | Submit an answer in the current session |
| GET | `/api/interview/report/{sessionId}` | Get the interview report (JSON) |
| GET | `/api/interview/report/{sessionId}/pdf` | Download the interview report (PDF) |
| GET | `/api/speech/token` | Get a short-lived Azure Speech auth token |

---

## Security Notes

- The backend issues short-lived Azure Speech tokens to the frontend — raw speech keys are never exposed to the browser.
- Never commit `Agents/.env`. It is excluded in `.gitignore`.

---

## License

MIT
