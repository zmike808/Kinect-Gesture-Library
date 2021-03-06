using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

public class Naive_Bayesian : MonoBehaviour {
	//record utilities
	bool train = false;	
	bool recording = false;
	string train_name = "";
	
	float best = 0f;
	
	//struct to keep track of an individual joint position at a given time
	public struct JointPositionSnapshot{
		public Vector3 position;
		public JointPositionSnapshot(Vector3 pos){	
			position = pos;
		}
	};
	
	string state = "none";
	public Texture2D[] states;	
	float reset_timer = 0.0f;
	
	//struct to help out
	
	//struct to handle arm states
	public struct ArmsSnapshot{
		public float timestamp;
		public JointPositionSnapshot[] left_joints;
		public JointPositionSnapshot[] right_joints;
		//calling this will take a snapshot of the arms and record the time
		public ArmsSnapshot(GameObject[] left, GameObject[] right){
			left_joints = new JointPositionSnapshot[3];
			right_joints = new JointPositionSnapshot[3];
			timestamp = Time.time;
			for(int i = 0; i < 3; i++){
				left_joints[i] = new JointPositionSnapshot(left[i].transform.position);
				right_joints[i] = new JointPositionSnapshot(right[i].transform.position);
			}
			
			//testing - print when we make a new state
			//Debug.Log("Making an arm snapshot - " + timestamp);
		}
		public bool CompareStates(GameObject[] left, GameObject[] right){
			//if anything has changed, return true, else return false
			for(int i = 0; i < 3; i++){
				if(left[i].transform.position != left_joints[i].position)
					return true;
				if(right[i].transform.position != right_joints[i].position)
					return true;
			}
			return false;	
		}
		public float GetLength(){
			return (left_joints[0].position - left_joints[1].position).magnitude
				+ (left_joints[1].position - left_joints[2].position).magnitude;			
		}
		public List <float> GenerateAngles(){
			List <float> angles = new List<float>();
			string line = "";
			
			Vector3 right_down = new Vector3(right_joints[2].position.x, right_joints[2].position.y - 1, right_joints[2].position.z);
			Vector3 left_down = new Vector3(left_joints[2].position.x, left_joints[2].position.y - 1, left_joints[2].position.z);
			//shoulder angles
			angles.Add(Vector3.Angle(left_joints[1].position-left_joints[2].position, left_down));
			angles.Add(Vector3.Angle(right_joints[1].position-right_joints[2].position, right_down));
			//elbow angles
			angles.Add(Vector3.Angle(left_joints[0].position - left_joints[1].position, 
				left_joints[2].position - left_joints[1].position));
			angles.Add(Vector3.Angle(right_joints[0].position - right_joints[1].position, 
				right_joints[2].position - right_joints[1].position));
			
			return angles;
			
		}
	};
	
	public class feature{
		public float mean;
		public float std_deviation;
		List <float> data = new List<float>();
		
		public feature(){}
		public feature(List <float> d){
			data = d;
			CalculateMean();
			CalculateStdDev();
			Debug.Log("Feature - Mean: " + mean + " Std Dev: " + std_deviation);
		}
		void CalculateMean(){
			float sum = 0;
			foreach(float d in data)
				sum += d;
			mean = sum/(float)data.Count;
		}
		
		void CalculateStdDev(){
			float sum_sqr_diff = 0;
			foreach(float d in data)
				sum_sqr_diff += Mathf.Pow(mean - d, 2);
			std_deviation = Mathf.Sqrt(sum_sqr_diff/data.Count);
		}
		public float CalculateProbability(float instance){
			//(1/sqrt(2*pi*std_dev^2)*e^(-(data - mean)^2/(2*std_dev^2))
			
			float alpha = 1/(Mathf.Sqrt(2*Mathf.PI*Mathf.Pow(std_deviation, 2)));
			float beta = -((Mathf.Pow(instance - mean, 2)/(2*Mathf.Pow(std_deviation, 2))));
				//Mathf.Exp((-Mathf.Pow(instance - mean, 2))/(2*Mathf.Pow(std_deviation, 2)));
			
			/*if(alpha*beta > .3f)
				Debug.Log("close " + instance + " " + mean);
			*/
			
			float prob = alpha*Mathf.Exp(beta);
			
			if(prob < .1f)
				return .1f;
			return prob;
		}
		public void AddData(float val){
			data.Add(val);
			CalculateMean();
			CalculateStdDev();
		}
	};
	
	
	public class BayesianClassifier{
		public string pose_name;
		List <feature> features = new List<feature>();
		
		
		public BayesianClassifier(){}
		public BayesianClassifier(string n, List<List<float>> all_data){
			pose_name = n;
			for(int i = 0; i < all_data.Count; i++){
				features.Add(new feature(all_data[i]));
			}
		}
		public float GetProbablity(List<float> instance){
			float prob = 1;
			string line = pose_name;
			for(int i = 0; i < features.Count && i < instance.Count; i++){
				feature feat = features[i];
				float new_prob = feat.CalculateProbability(instance[i]);
				
				line += " " + new_prob;
					
//					features[i].CalculateProbablity(instance[i]);
				if(new_prob < .1f)
					new_prob = .1f;
				prob*=new_prob;
			}
			
			line += ": " + prob;
			//Debug.Log(line);
			return prob;
		}
		
		public void AddData(List<float> new_data){
			for(int i = 0; i < features.Count; i++)
				features[i].AddData(new_data[i]);
			
		}
		
	};
	
	
	//we're going to keep track of the arm joints to interpret movement
	//and relay that the DropletMovement
	public GameObject [] left_arm;
	public GameObject [] right_arm;
	
	//we're going to save a queue of positions for now and analyze recent strings
	//for movements we are checking for
	private List <ArmsSnapshot> arm_states = new List<ArmsSnapshot>();
	List <ArmsSnapshot> recorded_states = new List<ArmsSnapshot>();
	List <BayesianClassifier> classifiers = new List<BayesianClassifier>();
	
	
	//store detected motions here
	public List <string> motions = new List<string>();
	public bool updated = false;
	
	void RecordValues(){
		TextWriter tw = new StreamWriter("Assets\\bayes_data.txt", true);
		
		foreach(ArmsSnapshot shot in recorded_states){
			List<float> angles = shot.GenerateAngles();
			string line = train_name;
			foreach(float angle in angles)
				line += " " + angle;
			
			//Debug.Log(line);
			tw.WriteLine(line);
		}
		
		tw.Close();
	}
	
	int identification = 0;
	float best_prob = 0;
	List <float> best_data = new List<float>();
	bool best_changed = false;
	string best_name = "";
	
	void OnGUI(){
		if(train){
			train_name = GUI.TextField(new Rect(25, 100, 100, 25), train_name);
			if(recording){
				if(GUI.Button(new Rect(Screen.width - 150, 50, 100, 50), "Rec"))
					recorded_states.Add(new ArmsSnapshot(left_arm, right_arm));
				
				if(GUI.Button(new Rect(25, 25, 100, 50), "Stop and Save")){
					RecordValues();
					recording = false;
				}
				if(GUI.Button(new Rect(150, 25, 100, 50), "Stop (Don't Save)"))
					recording = false;	
			}
			else{
				if(GUI.Button(new Rect(25, 25, 100, 50), "Record"))
					recording = true;
			}
			if(GUI.Button(new Rect(275, 25, 100, 50), "Stop Training"))
				train = false;
		}
		else{
			if(GUI.Button(new Rect(25, 25, 100, 50), "Train"))
				train = true;
			if(GUI.Button(new Rect(150, 25, 100, 50), "Update Library"))
				ReadFile();
			
			ArmsSnapshot cur_state = arm_states[arm_states.Count-1];
			List <float> angles = cur_state.GenerateAngles();
			string line = "";
			foreach(float angle in angles)
				line += angle + " ";
			GUI.Label(new Rect(50, Screen.height - 100, 200, 50), line);
			for(int i = 0; i < classifiers.Count; i++){
				if(classifiers[i].GetProbablity(angles) > best_prob){
					identification	= i;
					best_data = angles;
					best_changed = true;
					best_prob = classifiers[i].GetProbablity(angles);
					best_name = classifiers[i].pose_name;
				}
				
				
				string l = classifiers[i].pose_name + " "  + classifiers[i].GetProbablity(angles);
				GUI.Label(new Rect(50, Screen.height - 150 - 50*i, 200, 50), l);	
			}
		}
		
		if(best_changed){
			GUI.Label(new Rect(50, 200, 500, 50), "Best " + best_name + " is " + best_prob + ". Accept?");
			if(GUI.Button(new Rect(50, 250, 100, 25), "Accept")){
				classifiers[identification].AddData(best_data);
				best_changed = false;
				RecordValues();
			}
			if(GUI.Button(new Rect(50, 250, 100, 25), "Reject"))
				best_changed = false;
			best_prob = 0;
			
		}
		
	}
	
	
	
	void ReadFile(){
		TextReader tr = new StreamReader("Assets\\bayes_data.txt");
		classifiers.Clear();
		
		string line = tr.ReadLine();
		
		while(line != null){
			//need to separate each feature into a different list, each different name will be associated with different instance of classifier
			string[] sline = line.Split(' ');
			string pose_name = sline[0];
			List<List<float>> data = new List<List<float>>();
			for(int i = 1; i < sline.GetLength(0); i++)
				data.Add(new List<float>());
			
			while(line!= null && pose_name == sline[0]){
				for(int i = 1; i < sline.GetLength(0); i++)
					data[i-1].Add(float.Parse(sline[i]));				
				
				line = tr.ReadLine();
				if(line != null)
					sline = line.Split(' ');
			}
			
			//save current data to new classifier
			classifiers.Add(new BayesianClassifier(pose_name, data));
		}
		
		tr.Close();
	}	
	
	// Use this for initialization
	void Start () {		
		//save our first positions
		arm_states.Add(new ArmsSnapshot(left_arm, right_arm));
		
	}
	
	// Update is called once per frame
	void Update () {
		updated = false;
		
		//check for a new arm state
		if(recording){
			;
			//recorded_states.Add(new ArmsSnapshot(left_arm, right_arm));
		}
		if(!train){
			DetectMovement();
			InterpretGestures();			
		}
		
		
	}
	
	
	void Decay(){
		for(int i = 0; i < arm_states.Count; i++){
			if(arm_states[i].timestamp > Time.time - 10f)
				return;
			else{
				arm_states.RemoveAt(i);
				i--;
			}
		}
	}
	
	
	void DetectMovement(){
		if(arm_states.Count > 0){
		//check to see if any of the positions changed before we record anything
			if(arm_states[arm_states.Count-1].CompareStates(left_arm, right_arm)){
				arm_states.Add(new ArmsSnapshot(left_arm, right_arm));
				updated = true;	
			}
		}
	}
	
	void InterpretGestures(){
		//note: for the rush segments, we care more about current state
		//than changes in state, but other games will be slightly different
		//Debug.Log(arm_states.Count-1);
		
		
		if(arm_states.Count == 0)
			return;
		
		ArmsSnapshot cur_state = arm_states[arm_states.Count-1];
		/*float left_out;
		float right_out;
		float arm_length = cur_state.GetLength();*/
		
		//order is hand, elbow, shoulder
		//we're going to generate angles on elbow joint and shoulder joint
		if(!train){
			List <float> angles = cur_state.GenerateAngles();
			foreach(BayesianClassifier classifier in classifiers){
				if(best < classifier.GetProbablity(angles)){
					best = classifier.GetProbablity(angles);
					Debug.Log(classifier.pose_name + " " + best);
				}
				
				//Debug.Log(classifier.pose_name + " " + classifier.GetProbablity(angles));
				if(classifier.GetProbablity(angles) > .001)
					Debug.Log("POSE DETECTED: " + classifier.pose_name);			
			}
		}
		
		
		
/*		//reset droplet movements
		droplet.back = droplet.left = droplet.right = droplet.up = droplet.down = 0f;
		
		//left_out and right_out are going to be on a scale from 0 to 1
		//	0 all the way in, 1 all the way out
		left_out = (cur_state.left_joints[2].position.x - cur_state.left_joints[0].position.x)/arm_length;
		right_out = (cur_state.right_joints[0].position.x - cur_state.right_joints[2].position.x)/arm_length;
		
		
		//back, left, right
		//back - both hands forward
		if(left_out < 0.5 && right_out < 0.5){
			droplet.back = 1 - (left_out + right_out)/2;
			state = "stop";	
		}
		//left - both arms out, left down, right up
		else if(cur_state.left_joints[0].position.y < cur_state.left_joints[2].position.y - 3*arm_length/4
		   && cur_state.right_joints[0].position.y > cur_state.right_joints[2].position.y - arm_length/4){
			droplet.left = (cur_state.left_joints[2].position.y - cur_state.left_joints[0].position.y)/arm_length;
			state = "left";
		}
		//right - both arms out, left up, right down
		else if(cur_state.left_joints[0].position.y > cur_state.left_joints[2].position.y - arm_length/4
		         && cur_state.right_joints[0].position.y < cur_state.right_joints[2].position.y - 3*arm_length/4){
			droplet.right = (cur_state.right_joints[2].position.y - cur_state.right_joints[0].position.y)/arm_length;
			state = "right";	
		}
		
		//INVERTED controls - up and down
		else if(cur_state.left_joints[0].position.y < cur_state.left_joints[2].position.y - 3*arm_length/4
		   && cur_state.right_joints[0].position.y < cur_state.right_joints[2].position.y - 3*arm_length/4){
			//set their up movement based on extremity of their arm motion
			float displacement = (cur_state.left_joints[2].position.y - cur_state.left_joints[0].position.y);
			droplet.up = displacement/arm_length;
			state = "up";
		}
		
		
		else if(cur_state.left_joints[0].position.y > cur_state.left_joints[2].position.y - arm_length/4
		   && cur_state.right_joints[0].position.y > cur_state.right_joints[2].position.y - arm_length/4){
			float displacement = (cur_state.left_joints[0].position.y - cur_state.left_joints[2].position.y);
			droplet.down = displacement/arm_length;
			state = "down";
		}
		else
			state = "none";
		
		//RESET APPLICATION CONTROLS
		Vector3 arm = cur_state.right_joints[0].position - cur_state.right_joints[2].position;
		Vector3 down = new  Vector3(cur_state.right_joints[2].position.x, 
			cur_state.right_joints[2].position.y-1,
			cur_state.right_joints[2].position.z) - cur_state.right_joints[2].position;
		Vector3 larm = cur_state.left_joints[0].position - cur_state.left_joints[2].position;
		Vector3 ldown = new  Vector3(cur_state.left_joints[2].position.x, 
			cur_state.left_joints[2].position.y-1,
			cur_state.left_joints[2].position.z) - cur_state.left_joints[2].position;
		
		
		//get angle between the vectors
		float angle = Vector3.Angle(down, arm);
		//Debug.Log(right_out + " Angle is " + angle + " " + reset_timer + " " + Time.deltaTime);
		float langle = Vector3.Angle(ldown, larm);
		
		//arm must be seventy percent extended and shoulder to hand is twenty to forty degrees from downwards
		if(right_out > .5 && (5 < angle && angle < 60) && langle < 15 ){
			reset_timer += Time.deltaTime;
			state = "reset";
		}
		else
			reset_timer = 0f;
		
		
		
		
		//there is an arm forward and it is tilted skyward, go up
		if(left_out < 0.3f){
			if(cur_state.left_joints[0].position.y > cur_state.left_joints[2].position.y)
				droplet.up = 1f;
			else if(cur_state.left_joints[0].position.y < cur_state.left_joints[2].position.y)
				droplet.down = 1f;
		}
		//there is an arm forward and tilted down, go down
		else if(right_out < 0.3f){
			if(cur_state.right_joints[0].position.y > cur_state.right_joints[2].position.y)
				droplet.up = 1f;
			else if(cur_state.right_joints[0].position.y < cur_state.right_joints[2].position.y)
				droplet.down = 1f;
		}*/
		
	}
}
