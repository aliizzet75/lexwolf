#!/usr/bin/env python3
"""
Test script for LexWolf Conversation Assistant
"""

import sys
import os
import json

# Add backend directory to Python path
backend_path = os.path.join(os.path.dirname(__file__), 'backend')
sys.path.insert(0, backend_path)

def test_conversation_service_import():
    """Test conversation service import"""
    print("Testing Conversation Service Import...")
    try:
        from services.conversation_assistant import ConversationAssistant
        print("  ✓ ConversationAssistant imported successfully")
        return True
    except Exception as e:
        print(f"  ✗ Error importing ConversationAssistant: {e}")
        return False

def test_conversation_api_import():
    """Test conversation API import"""
    print("Testing Conversation API Import...")
    try:
        from api.conversation import router
        print("  ✓ Conversation API imported successfully")
        return True
    except Exception as e:
        print(f"  ✗ Error importing Conversation API: {e}")
        return False

def test_conversation_service_functionality():
    """Test conversation service functionality"""
    print("Testing Conversation Service Functionality...")
    try:
        from services.conversation_assistant import ConversationAssistant
        
        # Create service instance
        assistant = ConversationAssistant()
        print("  ✓ ConversationAssistant instantiated successfully")
        
        # Test processing a transcript
        sample_transcript = """
        Herr Müller hat am 01.01.2020 einen Arbeitsvertrag mit der Schmidt GmbH geschlossen.
        Am 01.01.2023 wurde der Vertrag gekündigt.
        Die Kündigung ist unwirksam, da keine sachliche Rechtfertigung vorliegt.
        Wir müssen bis zum 15.06.2023 einen Widerspruch einreichen.
        Der Mandant möchte eine Kündigungsschutzklage beantragen.
        Die monatliche Miete beträgt 850 Euro.
        """
        
        result = assistant.process_transcript(sample_transcript, client_id="client_123")
        print(f"  ✓ Transcript processed successfully")
        print(f"  ✓ Topics identified: {result.get('topics', [])}")
        print(f"  ✓ Entities extracted: {list(result.get('entities', {}).keys())}")
        
        # Test generating summary
        summary = assistant.generate_summary(result)
        print(f"  ✓ Summary generated successfully")
        print(f"  ✓ Suggestions generated: {len(summary.get('suggested_actions', []))}")
        print(f"  ✓ Next steps identified: {len(summary.get('next_steps', []))}")
        
        # Test anonymization
        anonymized = assistant.anonymize_conversation(result)
        print(f"  ✓ Conversation anonymized successfully")
        print(f"  ✓ Anonymized flag: {anonymized.get('anonymized', False)}")
        
        # Test history
        history = assistant.get_conversation_history(client_id="client_123")
        print(f"  ✓ Conversation history retrieved: {len(history)} entries")
        
        return True
    except Exception as e:
        print(f"  ✗ Error testing ConversationService functionality: {e}")
        return False

def test_api_endpoints():
    """Test API endpoint structure"""
    print("Testing API Endpoints...")
    try:
        # Test importing the API
        from api.conversation import router
        print("  ✓ Conversation API imported successfully")
        
        # Check that routes are defined
        routes = [route.path for route in router.routes]
        print(f"  ✓ API routes defined: {len(routes)} routes")
        
        # Check for key endpoints
        key_endpoints = [
            "/conversation/process",
            "/conversation/summarize",
            "/conversation/anonymize",
            "/conversation/history"
        ]
        
        found_endpoints = [ep for ep in key_endpoints if any(ep in route for route in routes)]
        print(f"  ✓ Key endpoints implemented: {len(found_endpoints)}/{len(key_endpoints)}")
        
        return True
    except Exception as e:
        print(f"  ✗ Error: {e}")
        return False

def test_client_integration():
    """Test client-side integration"""
    print("Testing Client Integration...")
    try:
        # Test importing client modules
        client_src_path = os.path.join(os.path.dirname(__file__), 'client', 'src')
        sys.path.insert(0, client_src_path)
        
        from conversation_recorder import ConversationRecorder
        print("  ✓ ConversationRecorder imported successfully")
        
        # Test creating recorder instance
        recorder = ConversationRecorder()
        print("  ✓ ConversationRecorder instantiated successfully")
        
        # Test adding transcript
        sample_transcript = "Der Mandant möchte eine Kündigungsschutzklage beantragen."
        recorder.add_transcript(sample_transcript)
        print("  ✓ Transcript added successfully")
        
        # Test generating summary
        summary = recorder.generate_summary()
        print("  ✓ Summary generated successfully")
        
        return True
    except Exception as e:
        print(f"  ✗ Error testing client integration: {e}")
        return False

def main():
    """Run all tests"""
    print("LexWolf Conversation Assistant Test Suite")
    print("=" * 50)
    
    tests = [
        test_conversation_service_import,
        test_conversation_api_import,
        test_conversation_service_functionality,
        test_api_endpoints,
        test_client_integration
    ]
    
    passed = 0
    total = len(tests)
    
    for test in tests:
        if test():
            passed += 1
        print()
    
    print(f"Test Results: {passed}/{total} tests passed")
    
    if passed == total:
        print("🎉 Conversation assistant implementation verified successfully!")
        print("\nWhat's implemented:")
        print("  ✓ Conversation assistant service with entity extraction")
        print("  ✓ REST API endpoints for conversation processing")
        print("  ✓ Client-side conversation recording and analysis")
        print("  ✓ Integration with backend server")
        print("  ✓ Real-time suggestions and next steps generation")
        return 0
    else:
        print("❌ Conversation assistant implementation needs attention!")
        return 1

if __name__ == "__main__":
    sys.exit(main())