using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{

	public float timeLimit = 30;

	private static GameManager instance;
	private GameState state;

	public Ring[] registeredRings;
	private int totalRingsCount = 0;
	private int passedRingsCount = 0;
	private float timeElapsed = 0;
	private float countdown = 3.0f;
	private bool showUI = true;
	private bool isPaused = false;
	private bool handsRecognized = true;

	[SerializeField]
	private GameObject[] shouldHideObjects;
	[SerializeField]
	private GameObject pauseUI;
	[SerializeField]
	private GameObject detectionWarningUI;
	[SerializeField]
	private GameObject gameOverUI;
	[SerializeField]
	private GameObject stallUI;
	[SerializeField]
	private GameObject countdownUI;
	[SerializeField]
	private GameObject clickSound;
	[SerializeField]
	private GameObject stallSound;

	private AudioSource stallAudioSource;

	public static GameManager getInstance()
	{
		return instance ?? FindObjectOfType<GameManager>();
	}

	void Start()
	{
		state = GameState.PREGAME;
		totalRingsCount = registeredRings.Length;
		gameOverUI.SetActive(false);
		pauseUI.SetActive(isPaused);
		detectionWarningUI.SetActive(false);
		stallUI.SetActive(false);
		stallAudioSource = stallSound.GetComponent<AudioSource>();
		CanvasManager.getInstance().updateTotalRingsText(totalRingsCount);
		CanvasManager.getInstance().updateCountdownUIText(countdown.ToString("0"));
		Debug.Log("totalRingsCount: " + totalRingsCount);
	}

	void Update()
	{
		if (Input.GetKeyDown(KeyCode.Escape) && state == GameState.RUNNING)
		{
			if (isPaused) resumeGame(GamePauseCause.USER);
			else pauseGame(GamePauseCause.USER);
		}
		if (state == GameState.IDLE || isPaused) return;
		if (!SocketManager.getInstance().getStatus() && state == GameState.RUNNING)
		{
			handsRecognized = false;
			pauseGame(GamePauseCause.GAME);
		}
		else
		{
			handsRecognized = true;
			resumeGame(GamePauseCause.GAME);
		}
		if (!handsRecognized) return;
		if (state == GameState.PREGAME)
		{
			CanvasManager.getInstance().updateCountdownUIText(countdown.ToString("0"));
			if (countdown < 0.5)
			{
				CanvasManager.getInstance().updateCountdownUIText("Go!");
			}
			if (countdown < 0)
			{
				state = GameState.RUNNING;
				AircraftController.getInstance().setControllable(true);
				countdownUI.SetActive(false);
			}
			countdown -= Time.deltaTime;
		}

		bool stall = AircraftController.getInstance().isInStall();
		stallUI.SetActive(stall);
		if (stall)
		{
			if (!stallAudioSource.isPlaying) stallAudioSource.Play();
		}
		else
		{
			stallAudioSource.Stop();
		}

		if (state == GameState.RUNNING)
		{
			// 다음에 통과해야 할 Ring 색상 변경
			MeshRenderer nextRingRenderer = registeredRings[passedRingsCount].GetComponentInChildren<MeshRenderer>();
			Color toChange = Color.red;
			toChange.a = 0.5f;
			nextRingRenderer.material.color = toChange;

			timeElapsed += Time.deltaTime;
			// Ring이 등록된 순서대로 통과해야하기에 (passedRingsCount + 1)번째 이후 Ring들의 경우 이미 통과하였더라도 hasPassed를 false로 변경하여 ignore
			for (int i = passedRingsCount + 1; i < totalRingsCount; i++) registeredRings[i].hasPassed = false;
			if (registeredRings[passedRingsCount].hasPassed) registeredRings[passedRingsCount++].gameObject.SetActive(false);

			// 게임 오버 확인 순서
			// 반드시 TimeLimit 조건 확인 후 링 조건 확인
			if (timeElapsed >= timeLimit) overGame(GameOverCause.TIME_OVER);
			if (passedRingsCount >= totalRingsCount) overGame(GameOverCause.COMPLETE);

			CanvasManager.getInstance().updateTimeDisplayText(timeElapsed);
			CanvasManager.getInstance().updateCurrentRingsText(passedRingsCount);
		}

		CanvasManager.getInstance().updateGameStateText(state.ToString());
	}

	void overGame(GameOverCause cause)
	{
		Debug.Log("overGame called with cause: " + cause);
		toggleUI();
		gameOverUI.SetActive(true);
		stopAllAudioSources();
		string desc = "";
		switch (cause)
		{
			case GameOverCause.TIME_OVER:
				desc = "Time Over!";
				break;
			case GameOverCause.COMPLETE:
				desc = "Completed!";
				break;
		}

		CanvasManager.getInstance().updateGameOverUITitleText(desc);
		AircraftController.getInstance().setControllable(false);
		state = GameState.IDLE;
	}

	void pauseGame(GamePauseCause cause)
	{
		if (!isPaused && cause == GamePauseCause.USER)
		{
			isPaused = true;
			toggleUI();
			pauseUI.SetActive(true);
		}
		else if (!handsRecognized && cause == GamePauseCause.GAME)
		{
			detectionWarningUI.SetActive(true);
		}

		stopAllAudioSources();
		Time.timeScale = 0f;
	}

	void resumeGame(GamePauseCause cause)
	{
		if (isPaused && cause == GamePauseCause.USER)
		{
			isPaused = false;
			toggleUI();
			pauseUI.SetActive(false);
		}
		else if (handsRecognized && cause == GamePauseCause.GAME)
		{
			detectionWarningUI.SetActive(false);
		}

		Time.timeScale = 1f;
	}

	void toggleUI()
	{
		showUI = !showUI;
		foreach (GameObject obj in shouldHideObjects) obj.SetActive(showUI);
	}

	public Vector3 getLatestRingPosition()
	{
		return passedRingsCount > 0 ? registeredRings[passedRingsCount - 1].transform.position : Vector3.zero;
	}

	public int getPassedRingsCount()
	{
		return passedRingsCount;
	}

	public int getTotalRingsCount()
	{
		Debug.Log("getTotalRingsCount called" + totalRingsCount);
		return totalRingsCount;
	}

	public bool isGameRunning()
	{
		return state == GameState.RUNNING;
	}

	public void resetGame()
	{
		StartCoroutine(waitAndLoadScene(SceneManager.GetActiveScene().buildIndex));
		Debug.Log("game reset");
	}

	public void backToMainMenu()
	{
		StartCoroutine(waitAndLoadScene(SceneManager.GetActiveScene().buildIndex - 1));
		Debug.Log("back to main");
	}

	private IEnumerator waitAndLoadScene(int sceneIdx)
	{
		yield return new WaitForSeconds(clickSound.GetComponent<AudioSource>().clip.length);
		SceneManager.LoadScene(sceneIdx);
	}

	private void stopAllAudioSources()
	{
		AudioSource[] audioSources = FindObjectsOfType(typeof(AudioSource)) as AudioSource[];
		foreach (AudioSource source in audioSources) source.Stop();
	}

	enum GameState
	{
		PREGAME,
		IDLE,
		RUNNING,
	}

	enum GameOverCause
	{
		TIME_OVER,
		COMPLETE,
	}

	enum GamePauseCause
	{
		USER,
		GAME
	}
}
