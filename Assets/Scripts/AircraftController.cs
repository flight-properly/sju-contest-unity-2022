using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AircraftController : MonoBehaviour
{

	public Rigidbody aircraftRigidbody;
	public float minSpeed = 10.0f;
	public float stallSpeed = 15.0f;
	public float defaultSpeed = 25.0f;
	public float currentSpeed = 25.0f;
	public float maxSpeed = 50.0f;
	public float yawSensitivity = 50.0f;
	public float pitchSensitivity = 150.0f;
	public float rollSensitivity = 150.0f;
	public float movementSmoothness = 4.0f;
	public float gravity = 5.0f;
	public float accelerationDiff = 20.0f;
	// +: Acceleration, 0: None, -: Deceleration
	public float throttle = 0.0f;
	public GameObject engineSound;
	public bool isDebug = false;

	private Vector3 currentRotation;
	private bool isStall = false;
	private Transform boosterObject;
	private Vector3 defaultSpawnPoint = new Vector3(500, 250, 500);
	private bool isControllable = false;
	private AudioSource engineAudioSource;

	private static AircraftController instance;

	public static AircraftController getInstance()
	{
		return instance ?? FindObjectOfType<AircraftController>();
	}

	void Start()
	{
		aircraftRigidbody = GetComponent<Rigidbody>();
		boosterObject = transform.Find("Booster");
		if (boosterObject == null) Debug.LogError("Could not find booster object.");
		boosterObject.gameObject.SetActive(false);
		engineAudioSource = engineSound.GetComponent<AudioSource>();
	}

	void Update()
	{
		if (Input.GetKeyDown(KeyCode.R) && GameManager.getInstance().isGameRunning())
		{
			Debug.Log("respawn called");
			respawnVehicle();
		}
	}

	void FixedUpdate()
	{
		if (!isControllable)
		{
			if (engineAudioSource.isPlaying) engineAudioSource.Stop();
			return;
		}
		float pitch, yaw, roll;
		bool acceleration, deceleration;
		if (SocketManager.getInstance().isReady())
		{
			pitch = SocketManager.getInstance().getPitchValue();
			yaw = SocketManager.getInstance().getYawValue();
			roll = SocketManager.getInstance().getRollValue();
			acceleration = SocketManager.getInstance().getThrottleValue() == 1;
		}
		else
		{
			pitch = Input.GetAxis("Vertical");
			yaw = Input.GetAxis("Horizontal");
			roll = -Input.GetAxis("Horizontal");
			acceleration = Input.GetKey(KeyCode.Period);
			deceleration = Input.GetKey(KeyCode.Comma);
		}
		throttle = acceleration ? +accelerationDiff : 0;
		if (throttle != 0)
		{
			if (!engineAudioSource.isPlaying) engineAudioSource.Play();
		}
		else
		{
			engineAudioSource.Stop();
		}
		// Debug.Log("status: " + SocketManager.getInstance().getStatus() + ", pitch: " + pitch + ", yaw: " + yaw + ", roll: " + roll + ", throttle: " + throttle);
		handleMovement(pitch * pitchSensitivity, yaw * yawSensitivity, roll * rollSensitivity, throttle);
	}

	void handleMovement(float pitch, float yaw, float roll, float throttle)
	{
		Vector3 toRotate = new Vector3(pitch, yaw, roll);

		// Stall
		if (currentSpeed <= stallSpeed)
		{
			isStall = true;
			// 지면 방향 향하는 rotation 값
			Quaternion targetRotation = Quaternion.Euler(90, transform.eulerAngles.y, transform.eulerAngles.z);
			// 현재 rotation 값과 targetRotation값의 차이
			// cf. https://forum.unity.com/threads/subtracting-quaternions.317649/
			Quaternion quaternionDiff = targetRotation * Quaternion.Inverse(transform.rotation);

			// 쿼터니언 값을 벡터로 변환
			Vector3 vectorDiff = quaternionDiff.eulerAngles;

			// 보정
			if (vectorDiff.x > 180) vectorDiff.x -= 360;
			if (vectorDiff.y > 180) vectorDiff.y -= 360;
			if (vectorDiff.z > 180) vectorDiff.z -= 360;
			vectorDiff.x = Mathf.Clamp(vectorDiff.x, -pitchSensitivity, pitchSensitivity);
			vectorDiff.y = Mathf.Clamp(vectorDiff.y, -yawSensitivity, yawSensitivity);
			vectorDiff.z = Mathf.Clamp(vectorDiff.z, -rollSensitivity, rollSensitivity);
			toRotate = vectorDiff;
		}
		else
		{
			isStall = false;
		}

		currentRotation = Vector3.Lerp(currentRotation, toRotate, movementSmoothness * Time.fixedDeltaTime);

		// 회전 적용
		aircraftRigidbody.MoveRotation(aircraftRigidbody.rotation * Quaternion.Euler(currentRotation * Time.fixedDeltaTime));

		// Speed
		float changeRate = 1;
		// 피치값에 따른 중력 임의로 추가
		float gravityBasedOnPitch = gravity * Mathf.Sin(transform.eulerAngles.x * Mathf.Deg2Rad);
		currentSpeed += gravityBasedOnPitch * Time.fixedDeltaTime;

		// 최대 / 최소 속력에 가까워 질수록 변화율 0에 수렴
		if (throttle > 0) changeRate = (maxSpeed - currentSpeed) / currentSpeed;
		else if (throttle < 0) changeRate = (currentSpeed - minSpeed) / currentSpeed;

		currentSpeed += throttle * changeRate * Time.fixedDeltaTime;

		// 가속 적용
		aircraftRigidbody.velocity = transform.forward * currentSpeed;

		// Booster visual
		if (throttle > 0) boosterObject.gameObject.SetActive(true);
		else boosterObject.gameObject.SetActive(false);

		CanvasManager.getInstance().updateSpeedMeterText(currentSpeed);
		CanvasManager.getInstance().updateThrottleMeterText(throttle);
	}

	// 충돌 감지
	private void OnCollisionEnter(Collision target)
	{
		Debug.Log("OnCollisionEnter: " + target.gameObject.name);
		respawnVehicle();
	}

	private void respawnVehicle()
	{
		if (!GameManager.getInstance().isGameRunning()) return;
		// Reset player position
		int passedRingsCount = GameManager.getInstance().getPassedRingsCount();
		Vector3 latestRingPosition = GameManager.getInstance().getLatestRingPosition();
		// TODO: replace const with actual aircraft size
		transform.position = passedRingsCount > 0 ? latestRingPosition + (Vector3.left * 60) + (Vector3.up * 55 * transform.localScale.y) + (Vector3.forward * -100) : defaultSpawnPoint;
		transform.rotation = Quaternion.identity;
		aircraftRigidbody.Sleep();
		currentSpeed = defaultSpeed;
		aircraftRigidbody.velocity = Vector3.zero;
	}

	public void setControllable(bool option)
	{
		isControllable = option;
	}

	public bool isInStall()
	{
		return isStall;
	}
}
