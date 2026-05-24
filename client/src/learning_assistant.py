import json
import os
from datetime import datetime
from typing import Dict, List
import numpy as np

class LearningAssistant:
    def __init__(self, profile_path="user_profile.json"):
        self.profile_path = profile_path
        self.user_profile = self._load_profile()
        self.decision_patterns = self.user_profile.get("decision_patterns", {})
        self.style_feedback = self.user_profile.get("style_feedback", [])
        self.client_memory = self.user_profile.get("client_memory", {})
        
    def _load_profile(self):
        """
        Load user profile from file
        """
        if os.path.exists(self.profile_path):
            try:
                with open(self.profile_path, 'r', encoding='utf-8') as f:
                    return json.load(f)
            except Exception as e:
                print(f"Error loading profile: {e}")
        
        # Return default profile
        return {
            "created": datetime.now().isoformat(),
            "decision_patterns": {},
            "style_feedback": [],
            "client_memory": {},
            "statistics": {
                "documents_created": 0,
                "conversations_recorded": 0,
                "decisions_made": 0
            }
        }
    
    def _save_profile(self):
        """
        Save user profile to file
        """
        try:
            with open(self.profile_path, 'w', encoding='utf-8') as f:
                json.dump(self.user_profile, f, ensure_ascii=False, indent=2)
            return True
        except Exception as e:
            print(f"Error saving profile: {e}")
            return False
    
    def record_decision(self, case_type: str, decision: str, context: Dict = None):
        """
        Record user decision for learning
        """
        # Update statistics
        self.user_profile["statistics"]["decisions_made"] += 1
        
        # Record decision pattern
        if case_type not in self.decision_patterns:
            self.decision_patterns[case_type] = {}
        
        if decision not in self.decision_patterns[case_type]:
            self.decision_patterns[case_type][decision] = {
                "count": 0,
                "contexts": [],
                "last_used": None
            }
        
        self.decision_patterns[case_type][decision]["count"] += 1
        self.decision_patterns[case_type][decision]["last_used"] = datetime.now().isoformat()
        
        if context:
            self.decision_patterns[case_type][decision]["contexts"].append(context)
        
        # Save profile
        self._save_profile()
    
    def record_style_feedback(self, original_text: str, corrected_text: str, feedback: str = None):
        """
        Record style feedback for learning
        """
        feedback_entry = {
            "timestamp": datetime.now().isoformat(),
            "original": original_text,
            "corrected": corrected_text,
            "feedback": feedback,
            "changes": self._analyze_changes(original_text, corrected_text)
        }
        
        self.style_feedback.append(feedback_entry)
        
        # Update statistics
        self.user_profile["statistics"]["documents_created"] += 1
        
        # Save profile
        self._save_profile()
    
    def _analyze_changes(self, original: str, corrected: str) -> Dict:
        """
        Analyze changes between original and corrected text
        """
        # Simple change analysis
        changes = {
            "length_diff": len(corrected) - len(original),
            "word_diff": len(corrected.split()) - len(original.split()),
            "has_passive_voice": "werden" in corrected.lower() or "worden" in corrected.lower(),
            "formal_indicators": self._count_formal_indicators(corrected)
        }
        return changes
    
    def _count_formal_indicators(self, text: str) -> int:
        """
        Count formal language indicators
        """
        formal_words = ["gemäß", "hinsichtlich", "bezüglich", "sachgerecht", "zweckmäßig"]
        return sum(1 for word in formal_words if word in text.lower())
    
    def record_client_interaction(self, client_id: str, interaction_data: Dict):
        """
        Record client interaction for memory
        """
        if client_id not in self.client_memory:
            self.client_memory[client_id] = {
                "interactions": [],
                "preferences": {},
                "case_history": []
            }
        
        interaction_data["timestamp"] = datetime.now().isoformat()
        self.client_memory[client_id]["interactions"].append(interaction_data)
        
        # Update statistics
        self.user_profile["statistics"]["conversations_recorded"] += 1
        
        # Save profile
        self._save_profile()
    
    def get_decision_suggestion(self, case_type: str, context: Dict = None) -> str:
        """
        Get decision suggestion based on learned patterns
        """
        if case_type not in self.decision_patterns:
            return None
        
        # Find most common decision for this case type
        decisions = self.decision_patterns[case_type]
        if not decisions:
            return None
        
        # Sort by count (most frequent first)
        sorted_decisions = sorted(decisions.items(), key=lambda x: x[1]["count"], reverse=True)
        
        # Return most frequent decision
        return sorted_decisions[0][0] if sorted_decisions else None
    
    def get_style_suggestion(self, text_sample: str) -> Dict:
        """
        Get style suggestions based on learned feedback
        """
        if not self.style_feedback:
            return {"suggestion": "No style feedback available yet"}
        
        # Simple approach: look for similar patterns in feedback
        suggestions = []
        
        # Check for passive voice
        if "werden" in text_sample.lower() or "worden" in text_sample.lower():
            suggestions.append("Consider using active voice where possible")
        
        # Check for formal language
        formal_score = self._count_formal_indicators(text_sample)
        if formal_score < 2:
            suggestions.append("Consider adding more formal legal terminology")
        
        return {
            "suggestions": suggestions,
            "based_on_feedback": len(self.style_feedback)
        }
    
    def get_client_history(self, client_id: str) -> Dict:
        """
        Get client history and preferences
        """
        return self.client_memory.get(client_id, {
            "interactions": [],
            "preferences": {},
            "case_history": []
        })
    
    def get_statistics(self) -> Dict:
        """
        Get user statistics
        """
        return self.user_profile.get("statistics", {
            "documents_created": 0,
            "conversations_recorded": 0,
            "decisions_made": 0
        })
    
    def get_learning_summary(self) -> Dict:
        """
        Get learning summary
        """
        return {
            "profile_created": self.user_profile.get("created"),
            "statistics": self.get_statistics(),
            "decision_patterns_count": len(self.decision_patterns),
            "style_feedback_count": len(self.style_feedback),
            "clients_tracked": len(self.client_memory)
        }
    
    def reset_profile(self):
        """
        Reset user profile
        """
        self.user_profile = {
            "created": datetime.now().isoformat(),
            "decision_patterns": {},
            "style_feedback": [],
            "client_memory": {},
            "statistics": {
                "documents_created": 0,
                "conversations_recorded": 0,
                "decisions_made": 0
            }
        }
        self._save_profile()

# Example usage
if __name__ == "__main__":
    assistant = LearningAssistant()
    
    # Record some decisions
    assistant.record_decision("kündigung", "klage_einreichen", {
        "employment_duration": "3 years",
        "reason": "no valid grounds"
    })
    
    assistant.record_decision("kündigung", "klage_einreichen", {
        "employment_duration": "2 years",
        "reason": "no valid grounds"
    })
    
    assistant.record_decision("mietrecht", "außergerichtliche_einigung", {
        "dispute_amount": "500 EUR",
        "tenant_history": "good"
    })
    
    # Record style feedback
    assistant.record_style_feedback(
        "The contract was terminated by the employer.",
        "Der Arbeitsvertrag wurde vom Arbeitgeber gekündigt.",
        "Better to use German legal terminology"
    )
    
    # Record client interaction
    assistant.record_client_interaction("client_123", {
        "interaction_type": "consultation",
        "topics": ["kündigung", "abfindung"],
        "duration": "45 minutes"
    })
    
    # Get suggestions
    suggestion = assistant.get_decision_suggestion("kündigung")
    print(f"Suggested decision for 'kündigung': {suggestion}")
    
    style_suggestion = assistant.get_style_suggestion("The contract was terminated")
    print(f"Style suggestion: {style_suggestion}")
    
    # Get statistics
    stats = assistant.get_statistics()
    print(f"Statistics: {stats}")
    
    # Get learning summary
    summary = assistant.get_learning_summary()
    print(f"Learning summary: {summary}")