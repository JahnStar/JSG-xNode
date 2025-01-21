using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace XNode.Examples.StateGraph {
	[CreateAssetMenu(fileName = "New State Graph", menuName = "Samples/xNode Examples/State Graph")]
	public class StateGraph : NodeGraph {

		// The current "active" node
		public StateNode current;

		public void Continue() {
			current.MoveNext();
		}
	}
}