using UnityEngine;

/// <summary>
    /// Presentación uGUI de la partida de una pregunta para dos jugadores.
    /// </summary>
    public sealed class QuizUI : MonoBehaviour
    {
        private static readonly Color AnswerColor = new Color(0.08f, 0.28f, 0.62f, 1f);
        private static readonly Color CorrectColor = new Color(0.08f, 0.62f, 0.22f, 1f);
        private static readonly Color IncorrectColor = new Color(0.72f, 0.10f, 0.10f, 1f);

        [SerializeField] private QuizConnection connection;
        [SerializeField] private QuizGame game;
        [SerializeField] private UnityEngine.UI.Button buzzerButton;
        [SerializeField] private UnityEngine.UI.Text statusText;
        [SerializeField] private UnityEngine.UI.Text questionText;
        [SerializeField] private UnityEngine.UI.Button[] answerButtons;
        [SerializeField] private UnityEngine.UI.Text[] answerLabels;

        private bool isBound;
        private bool startRequested;

        private void OnEnable()
        {
            Bind();
        }

        private void Start()
        {
            if (!HasRequiredReferences())
            {
                enabled = false;
                return;
            }

            Refresh();
            connection.ConnectAndJoin();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void Bind()
        {
            if (isBound)
            {
                return;
            }

            if (connection != null)
            {
                connection.StateChanged += Refresh;
            }

            if (game != null)
            {
                game.StateChanged += Refresh;
            }

            if (buzzerButton != null)
            {
                buzzerButton.onClick.AddListener(PressBuzzer);
            }

            if (answerButtons != null && answerButtons.Length == 4)
            {
                answerButtons[0]?.onClick.AddListener(SubmitAnswerA);
                answerButtons[1]?.onClick.AddListener(SubmitAnswerB);
                answerButtons[2]?.onClick.AddListener(SubmitAnswerC);
                answerButtons[3]?.onClick.AddListener(SubmitAnswerD);
            }

            isBound = true;
        }

        private void Unbind()
        {
            if (!isBound)
            {
                return;
            }

            if (connection != null)
            {
                connection.StateChanged -= Refresh;
            }

            if (game != null)
            {
                game.StateChanged -= Refresh;
            }

            if (buzzerButton != null)
            {
                buzzerButton.onClick.RemoveListener(PressBuzzer);
            }

            if (answerButtons != null && answerButtons.Length == 4)
            {
                answerButtons[0]?.onClick.RemoveListener(SubmitAnswerA);
                answerButtons[1]?.onClick.RemoveListener(SubmitAnswerB);
                answerButtons[2]?.onClick.RemoveListener(SubmitAnswerC);
                answerButtons[3]?.onClick.RemoveListener(SubmitAnswerD);
            }

            isBound = false;
        }

        private void PressBuzzer()
        {
            game.PressBuzzer();
        }

        private void SubmitAnswerA() => game.SubmitAnswer(0);
        private void SubmitAnswerB() => game.SubmitAnswer(1);
        private void SubmitAnswerC() => game.SubmitAnswer(2);
        private void SubmitAnswerD() => game.SubmitAnswer(3);

        private void Refresh()
        {
            if (!HasRequiredReferences(false))
            {
                return;
            }

            questionText.text = game.Question;

            for (int index = 0; index < answerButtons.Length; index++)
            {
                answerLabels[index].text = $"{(char)('A' + index)}) {game.Answers[index]}";
                answerButtons[index].interactable = game.CanLocalPlayerAnswer;
                answerButtons[index].image.color = AnswerColor;
            }

            if (game.State == QuizMatchState.Finished)
            {
                answerButtons[game.CorrectAnswerIndex].image.color = CorrectColor;

                if (!game.AnswerWasCorrect && game.SelectedAnswerIndex >= 0)
                {
                    answerButtons[game.SelectedAnswerIndex].image.color = IncorrectColor;
                }
            }

            bool inRoom = connection.IsInRoom;
            buzzerButton.interactable = inRoom && game.CanPressBuzzer;
            statusText.text = inRoom ? game.StatusText : connection.Status;

            if (!inRoom)
            {
                startRequested = false;
                return;
            }

            if (!startRequested && game.MatchId == 0 && game.CanStartMatch)
            {
                startRequested = true;
                game.StartMatch();
            }
        }

        private bool HasRequiredReferences(bool logError = true)
        {
            bool valid = connection != null &&
                game != null &&
                buzzerButton != null &&
                statusText != null &&
                questionText != null &&
                answerButtons != null &&
                answerButtons.Length == 4 &&
                answerLabels != null &&
                answerLabels.Length == 4;

            if (valid)
            {
                for (int index = 0; index < 4; index++)
                {
                    if (answerButtons[index] == null || answerLabels[index] == null)
                    {
                        valid = false;
                        break;
                    }
                }
            }

            if (!valid && logError)
            {
                Debug.LogError("OneQuestionQuizUI tiene referencias de UI incompletas.", this);
            }

            return valid;
        }

    }
